// <copyright file="PreviewController.cs" company="b0w3rb1rd">
//           ▄▄▄▄     ▄▄▄▄     ▄▄▄▄
//     ▄▄▄▄▄▄█  █▄▄▄▄▄█  █▄▄▄▄▄█  █
//     █__ --█  █__ --█    ◄█  -  █
//     █▄▄▄▄▄█▄▄█▄▄▄▄▄█▄▄█▄▄█▄▄▄▄▄█
//   ┍━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━ ━  ━┉   ┉     ┉
//   │ Copyright (c) 2026 b0w3rb1rd.
//   │
//   │ This program is free software: you can redistribute it and/or modify
//   │ it under the terms of the GNU Affero General Public License as published
//   │ by the Free Software Foundation, version 3.
//   │
//   │ This program is distributed in the hope that it will be useful,
//   │ but WITHOUT ANY WARRANTY; without even the implied warranty of
//   │ MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   │ GNU Affero General Public License for more details.
//   │
//   │ You should have received a copy of the GNU Affero General Public License
//   │ along with this program.  If not, see https://www.gnu.org/licenses/.
//   │
//   │ This program is distributed with Additional Terms pursuant to Section 7
//   │ of the AGPLv3.  See the LICENSE file in the root directory of this
//   │ project for the complete terms and conditions.
//   │
//   │ https://slskd.org
//   │
//   ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌ ╌ ╌╌╌╌ ╌
//   │ SPDX-FileCopyrightText: b0w3rb1rd
//   │ SPDX-License-Identifier: AGPL-3.0-only
//   ╰───────────────────────────────────────────╶──── ─ ─── ─  ── ──┈  ┈
// </copyright>

namespace slskd.Sillon
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Asp.Versioning;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Serilog;
    using Soulseek;

    /// <summary>
    ///     Extraits d'écoute (ajout de sillon) : télécharge une portion d'un fichier distant, à partir d'un décalage
    ///     donné, puis interrompt le transfert. Le protocole Soulseek permet de reprendre un transfert à une position
    ///     quelconque ; on s'en sert pour n'obtenir que quelques secondes de musique.
    /// </summary>
    [Route("api/v{version:apiVersion}/sillon")]
    [ApiVersion("0")]
    [ApiController]
    [Produces("application/octet-stream", "application/json")]
    [Consumes("application/json")]
    public class PreviewController : ControllerBase
    {
        /// <summary>Taille maximale d'un extrait (≈ 45 s de FLAC 16/44).</summary>
        private const long MaxLength = 6 * 1024 * 1024;

        private static readonly SemaphoreSlim OneAtATime = new(1, 1);

        public PreviewController(ISoulseekClient soulseekClient)
        {
            Client = soulseekClient;
        }

        private ISoulseekClient Client { get; }
        private ILogger Log { get; } = Serilog.Log.ForContext<PreviewController>();

        /// <summary>
        ///     Télécharge <c>length</c> octets du fichier à partir de <c>offset</c>.
        /// </summary>
        /// <response code="200">Les octets demandés (moins si le fichier se termine avant).</response>
        /// <response code="404">Le pair est hors ligne ou le fichier n'est pas partagé.</response>
        /// <response code="409">Le pair a refusé le transfert (fichier verrouillé, pair qui nous ignore…).</response>
        /// <response code="429">Un autre extrait est déjà en cours.</response>
        /// <response code="504">Le pair ne nous a pas servi à temps (file d'attente).</response>
        [HttpPost("preview")]
        [Authorize(Policy = AuthPolicy.Any)]
        public async Task<IActionResult> Preview([FromBody, Required] PreviewRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Filename) || request.Offset < 0 || request.Length <= 0 || request.Length > MaxLength)
            {
                return BadRequest("Paramètres d'extrait invalides.");
            }

            // Un seul extrait à la fois : on ne sollicite pas les pairs en rafale.
            if (!await OneAtATime.WaitAsync(0))
            {
                return StatusCode(429, "Un extrait est déjà en cours de récupération.");
            }

            var timeout = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds ?? 45, 5, 180));
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            cts.CancelAfter(timeout);
            var buffer = new CappedStream(request.Length, cts);
            var lastState = TransferStates.None;

            try
            {
                Log.Information("Extrait de {File} chez {Username} ({Length} octets à partir de {Offset})", request.Filename, request.Username, request.Length, request.Offset);

                await Client.DownloadAsync(
                    request.Username,
                    request.Filename,
                    () => Task.FromResult<Stream>(buffer),
                    size: request.Size,
                    startOffset: request.Offset,
                    options: new TransferOptions(
                        stateChanged: args => lastState = args.Transfer.State,
                        seekOutputStreamAutomatically: false,
                        disposeOutputStreamOnCompletion: false),
                    cancellationToken: cts.Token);
            }
            catch (Exception) when (buffer.Full)
            {
                // Coupure volontaire : on a ce qu'il faut.
            }
            catch (UserOfflineException)
            {
                return NotFound("Le pair est hors ligne.");
            }
            catch (TransferRejectedException ex)
            {
                return Conflict($"Le pair a refusé : {ex.Message}");
            }
            catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
            {
                var queued = lastState.HasFlag(TransferStates.Queued);
                return StatusCode(504, queued
                    ? "Le pair n'a pas de slot libre : la demande est restée en file d'attente."
                    : "Le pair n'a pas répondu à temps.");
            }
            catch (TransferException ex) when (buffer.Length > 0)
            {
                // Transfert interrompu par le pair après quelques données : on renvoie ce qu'on a.
                Log.Debug(ex, "Extrait partiel");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Échec de l'extrait : {Message}", ex.Message);
                return StatusCode(502, ex.Message);
            }
            finally
            {
                OneAtATime.Release();
            }

            return File(buffer.ToArray(), "application/octet-stream");
        }
    }

    public record PreviewRequest
    {
        public string Username { get; init; }
        public string Filename { get; init; }
        public long? Size { get; init; }
        public long Offset { get; init; }
        public long Length { get; init; }
        public int? TimeoutSeconds { get; init; }
    }

    /// <summary>
    ///     Flux mémoire qui annule le transfert dès qu'il a reçu la quantité voulue.
    /// </summary>
    internal sealed class CappedStream : MemoryStream
    {
        public CappedStream(long limit, CancellationTokenSource cts)
        {
            Limit = limit;
            Cts = cts;
        }

        public bool Full { get; private set; }
        private long Limit { get; }
        private CancellationTokenSource Cts { get; }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var room = (int)Math.Max(0, Limit - Length);
            base.Write(buffer[..Math.Min(room, buffer.Length)]);
            if (Length >= Limit && !Full)
            {
                Full = true;
                Cts.Cancel();
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }
}
