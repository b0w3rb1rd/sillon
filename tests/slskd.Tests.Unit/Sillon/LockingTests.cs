// <copyright file="LockingTests.cs" company="b0w3rb1rd">
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

namespace slskd.Tests.Unit.Sillon
{
    using System;
    using System.IO;
    using slskd.Sillon;
    using Xunit;

    public class LockingTests
    {
        [Fact]
        public void Tilde_Marque_Un_Partage_Verrouille()
        {
            var share = new slskd.Shares.Share("~/Users/x/Réservé aux amis");
            Assert.True(share.IsLocked);
            Assert.False(share.IsExcluded);
            Assert.Equal("/Users/x/Réservé aux amis", share.LocalPath);
            Assert.Equal("Réservé aux amis", share.RemotePath);
        }

        [Fact]
        public void Chemins_Verrouilles()
        {
            var locked = Locking.LockedRemotePaths(new[] { "/Users/x/Music", "~[Amis]/Users/x/Private", "!/Users/x/Music/Perso" });
            Assert.Equal(new[] { "Amis" }, locked);
            Assert.True(Locking.IsLocked(locked, "Amis\\Album\\01.flac"));
            Assert.True(Locking.IsLocked(locked, "Amis"));
            Assert.False(Locking.IsLocked(locked, "Amis2\\01.flac"));
            Assert.False(Locking.IsLocked(locked, "Music\\Album\\01.flac"));
        }

        [Fact]
        public void Liste_Amis_Relue_Quand_Le_Fichier_Change()
        {
            var file = Path.Combine(Path.GetTempPath(), $"sillon-buddies-{Guid.NewGuid()}.txt");
            Environment.SetEnvironmentVariable("SILLON_BUDDIES_FILE", file);
            try
            {
                Assert.False(Locking.IsBuddy("alice"));
                File.WriteAllText(file, "Alice\n  bob  \n");
                Assert.True(Locking.IsBuddy("alice"));
                Assert.True(Locking.IsBuddy("BOB"));
                Assert.False(Locking.IsBuddy("carol"));
                File.WriteAllText(file, "carol\n");
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
                Assert.False(Locking.IsBuddy("alice"));
                Assert.True(Locking.IsBuddy("carol"));
            }
            finally
            {
                File.Delete(file);
                Environment.SetEnvironmentVariable("SILLON_BUDDIES_FILE", null);
            }
        }
    }
}
