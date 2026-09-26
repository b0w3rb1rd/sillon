// <copyright file="Locking.cs" company="b0w3rb1rd">
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
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    ///     Partages verrouillés (réservés aux amis), ajoutés par sillon, version modifiée de slskd.
    /// </summary>
    /// <remarks>
    ///     Un partage dont la chaîne commence par « ~ » est verrouillé : ses fichiers sont annoncés comme verrouillés
    ///     aux pairs qui ne sont pas des amis, et leurs demandes de téléchargement sont refusées. La liste d'amis est lue
    ///     dans le fichier désigné par la variable d'environnement SILLON_BUDDIES_FILE (un pseudo par ligne), relu
    ///     automatiquement quand il change.
    /// </remarks>
    public static class Locking
    {
        public const char LockedPrefix = '~';

        private static readonly object SyncRoot = new();
        private static DateTime buddiesStamp = DateTime.MinValue;
        private static HashSet<string> buddies = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     Chemins distants (alias) des partages verrouillés, d'après la configuration des partages.
        /// </summary>
        public static IReadOnlyList<string> LockedRemotePaths(IEnumerable<string> rawShares) =>
            (rawShares ?? Enumerable.Empty<string>())
                .Where(s => s.StartsWith(LockedPrefix))
                .Select(s => new Shares.Share(s))
                .Where(s => !s.IsExcluded)
                .Select(s => s.RemotePath)
                .ToList();

        /// <summary>
        ///     Indique si un chemin distant (fichier ou dossier) se trouve dans un partage verrouillé.
        /// </summary>
        public static bool IsLocked(IReadOnlyList<string> lockedRemotePaths, string remotePath)
        {
            if (lockedRemotePaths.Count == 0 || string.IsNullOrEmpty(remotePath))
            {
                return false;
            }

            return lockedRemotePaths.Any(root =>
                remotePath.Equals(root, StringComparison.OrdinalIgnoreCase)
                || remotePath.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase)
                || remotePath.StartsWith(root + '/', StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Indique si un pair figure dans la liste d'amis.
        /// </summary>
        public static bool IsBuddy(string username)
        {
            var file = Environment.GetEnvironmentVariable("SILLON_BUDDIES_FILE");

            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(username))
            {
                return false;
            }

            try
            {
                var stamp = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;

                lock (SyncRoot)
                {
                    if (stamp != buddiesStamp)
                    {
                        buddies = stamp == DateTime.MinValue
                            ? new(StringComparer.OrdinalIgnoreCase)
                            : new(File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase);
                        buddiesStamp = stamp;
                    }

                    return buddies.Contains(username);
                }
            }
            catch
            {
                // En cas de doute, on considère le pair comme non-ami : on ne livre rien de verrouillé.
                return false;
            }
        }
    }
}
