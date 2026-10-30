using Shared.Authorization;

namespace ClinicFlow.Tests.Authorization
{
    // The permission catalog and the default matrix are written twice: in docs/v2/02_ROLES_AND_PERMISSIONS.md
    // (§5, §6), which people read, and in Shared/Authorization, which the code checks. These tests read the
    // document and fail on any difference, so the two can't drift apart
    public class PermissionMatrixTests
    {
        private static readonly string[] Document = File.ReadAllLines(
            Path.Combine(RepositoryRoot(), "docs", "v2", "02_ROLES_AND_PERMISSIONS.md"));

        [Fact]
        public void Catalog_MatchesTheDocument()
        {
            var documented = TableRows("## 5. Permission catalog").Select(cells => Code(cells[1]));

            Assert.Equal(documented.Order(), Permissions.All.Order());
        }

        [Fact]
        public void ModuleOfEachPermission_MatchesTheDocument()
        {
            // ADR 0008 decision 3: the Module column of 02 §5 is authoritative, and every permission has exactly one
            var differences = TableRows("## 5. Permission catalog")
                .Select(cells => (Permission: Code(cells[1]), Documented: cells[2]))
                .Where(row => !Modules.OfPermissions.TryGetValue(row.Permission, out var coded) || coded != row.Documented)
                .Select(row => $"{row.Permission}: document \"{row.Documented}\", code {Modules.OfPermissions.GetValueOrDefault(row.Permission) ?? "none"}")
                .ToList();

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        [Fact]
        public void EveryPermission_BelongsToAKnownModule()
        {
            Assert.Equal(Permissions.All.Order(), Modules.OfPermissions.Keys.Order());
            Assert.All(Modules.OfPermissions.Values, module => Assert.Contains(module, Modules.All));
        }

        [Fact]
        public void ModuleOf_AnUnknownPermission_Throws()
            => Assert.Throws<InvalidOperationException>(() => Modules.Of("appointments.chek_in"));

        [Fact]
        public void Matrix_MatchesTheDocument_CellByCell()
        {
            var rows = TableRows("## 6. Role × permission matrix").ToList();
            Assert.Equal(Permissions.All.Order(), rows.Select(cells => Code(cells[0])).Order()); // one row per permission

            var differences = new List<string>();
            foreach (var cells in rows)
            {
                var permission = Code(cells[0]);
                for (var i = 0; i < Roles.All.Count; i++)
                {
                    var role = Roles.All[i];
                    var documented = ScopeIn(cells[i + 1]);
                    var coded = DefaultPermissionMatrix.Grants[role].TryGetValue(permission, out var scope) ? scope : (PermissionScope?)null;
                    if (documented != coded)
                        differences.Add($"{permission} / {role}: document \"{cells[i + 1]}\" → {documented?.ToString() ?? "none"}, code {coded?.ToString() ?? "none"}");
                }
            }

            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        }

        [Fact]
        public void Matrix_ColumnsAreInTheOrderOfRolesAll()
        {
            var header = Document.SkipWhile(l => !l.StartsWith("## 6.")).First(l => l.StartsWith("| Permission |"));

            Assert.Equal(Roles.All, Cells(header).Skip(1));
        }

        [Fact]
        public void ClinicalContent_IsNeverGrantedToAdminOrAccountant()
        {
            // ADR 0002 decision 6: a hard rule, not configuration
            foreach (var permission in Permissions.ClinicalContent)
            {
                Assert.DoesNotContain(permission, DefaultPermissionMatrix.Grants[Roles.Admin].Keys);
                Assert.DoesNotContain(permission, DefaultPermissionMatrix.Grants[Roles.Accountant].Keys);
                Assert.False(DefaultPermissionMatrix.CanGrant(Roles.Admin, permission));
                Assert.False(DefaultPermissionMatrix.CanGrant(Roles.Accountant, permission));
                Assert.True(DefaultPermissionMatrix.CanGrant(Roles.Doctor, permission));
            }
        }

        [Fact]
        public void ScopeFor_SeveralRoles_TheWidestWins()
        {
            // 02 §4: the effective permission is the union of the roles
            Assert.Equal(PermissionScope.Own, DefaultPermissionMatrix.ScopeFor([Roles.Doctor], Permissions.Appointments.View));
            Assert.Equal(PermissionScope.Branch, DefaultPermissionMatrix.ScopeFor([Roles.Doctor, Roles.Receptionist], Permissions.Appointments.View));
            Assert.Equal(PermissionScope.Organization, DefaultPermissionMatrix.ScopeFor([Roles.Manager, Roles.Admin], Permissions.Staff.View));
        }

        [Theory]
        [InlineData]
        [InlineData("Janitor")]
        [InlineData(Roles.Admin)] // Admin has no appointment permissions by default (02 §6)
        public void ScopeFor_RolesWithoutTheGrant_IsNull(params string[] roles)
            => Assert.Null(DefaultPermissionMatrix.ScopeFor(roles, Permissions.Appointments.View));

        // The rows of the first table after the heading, as trimmed cells
        private static IEnumerable<string[]> TableRows(string heading)
            => Document
                .SkipWhile(l => !l.StartsWith(heading))
                .Skip(1)
                .SkipWhile(l => !l.StartsWith('|'))
                .TakeWhile(l => l.StartsWith('|'))
                .Skip(2) // header and separator
                .Select(Cells);

        private static string[] Cells(string line) => line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

        private static string Code(string cell) => cell.Trim('`');

        // How a matrix cell reads, following the legend of 02 §6 and the rules in DefaultPermissionMatrix
        private static PermissionScope? ScopeIn(string cell)
        {
            if (cell.StartsWith('—') || cell == "public slots only") return null;
            if (cell.Contains("org managers only") || cell.Contains("medical director")) return null; // a staff flag, P1
            if (cell == "own position") return PermissionScope.Own;
            if (cell.StartsWith("B/O")) return PermissionScope.Branch; // widened by an organization assignment, P1
            return cell[0] switch
            {
                'O' => PermissionScope.Organization,
                'B' => PermissionScope.Branch,
                'S' => PermissionScope.Own,
                _ => throw new InvalidOperationException($"Unreadable matrix cell \"{cell}\""),
            };
        }

        private static string RepositoryRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "ClinicFlow.slnx")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("ClinicFlow.slnx not found above the test output");
        }
    }
}
