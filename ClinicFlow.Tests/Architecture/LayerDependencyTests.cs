using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Domain.Interfaces;
using Infrastructure.Extensions;
using Persistence.AppData;
using Presentation.Controllers;
using Services;
using Services.Abstraction.Contracts;
using Shared.ResultPattern;

namespace ClinicFlow.Tests.Architecture
{
    // Dependencies point inward (docs/v2/adr/0006, 06_TECHNICAL_DESIGN.md §2). The compiler lets a project use
    // anything it reaches transitively, so these tests read the types each layer's compiled code uses instead.
    // A project reference that no code uses doesn't count; only real use does.
    public class LayerDependencyTests
    {
        private static readonly Dictionary<string, Assembly> Layers = new[]
        {
            typeof(IUnitOfWork).Assembly,               // Domain
            typeof(Result).Assembly,                    // Shared
            typeof(IEmailService).Assembly,             // Services.Abstraction
            typeof(ServiceAssemblyReference).Assembly,  // Services
            typeof(ClinicDbContext).Assembly,           // Persistence
            typeof(ProviderServiceExtensions).Assembly, // Infrastructure
            typeof(ApiController).Assembly,             // Presentation
        }.ToDictionary(a => a.GetName().Name!);

        private static readonly string[] Jwt =
            ["System.IdentityModel.Tokens.Jwt", "Microsoft.IdentityModel.Tokens", "Microsoft.IdentityModel.JsonWebTokens"];

        // What each layer must not use: our other layers, plus packages that belong in another layer
        private static readonly Dictionary<string, string[]> Forbidden = new()
        {
            ["Domain"] = ["Shared", "Services.Abstraction", "Services", "Persistence", "Infrastructure", "Presentation"],
            ["Shared"] = ["Services.Abstraction", "Services", "Persistence", "Infrastructure", "Presentation"],
            ["Services.Abstraction"] = ["Services", "Persistence", "Infrastructure", "Presentation"],
            ["Services"] = ["Persistence", "Infrastructure", "Presentation", "Microsoft.EntityFrameworkCore", "MailKit", "MimeKit", .. Jwt],
            ["Persistence"] = ["Services", "Infrastructure", "Presentation"],
            ["Infrastructure"] = ["Services", "Persistence", "Presentation"],
            ["Presentation"] = ["Domain", "Services", "Persistence", "Infrastructure"],
        };

        // Allowed for enums only. Request and response DTOs carry Domain enums (Gender, BloodType, ...), so their
        // validators see them; any other Domain type in Presentation is a violation
        private static readonly HashSet<(string Layer, string Reference)> EnumsOnly =
        [
            ("Presentation", "Domain"),
        ];

        // Known violations, each with the PR that removes it. Empty since P0 PR 3; add one only with a plan to remove it
        private static readonly HashSet<(string Layer, string Reference)> KnownExceptions = [];

        public static TheoryData<string> LayerNames => [.. Forbidden.Keys];

        [Theory]
        [MemberData(nameof(LayerNames))]
        public void Layer_UsesOnlyAllowedAssemblies(string layer)
        {
            var violations = UsedTypes(layer)
                .Where(t => Forbidden[layer].Contains(t.Assembly))
                .Where(t => !KnownExceptions.Contains((layer, t.Assembly)))
                .Where(t => !(EnumsOnly.Contains((layer, t.Assembly)) && IsEnum(t.Assembly, t.Type)))
                .Select(t => $"{t.Assembly}: {t.Type}")
                .Distinct()
                .Order()
                .ToList();

            Assert.True(violations.Count == 0,
                $"{layer} must not use these (docs/v2/adr/0006):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        [Fact]
        public void KnownExceptions_AreAllStillViolations()
        {
            // Once a PR removes a violation, its exception has to go too, so it can't silently come back
            var stale = KnownExceptions
                .Where(e => !UsedTypes(e.Layer).Any(t => t.Assembly == e.Reference))
                .Select(e => $"{e.Layer} → {e.Reference}")
                .ToList();

            Assert.True(stale.Count == 0,
                $"Remove these from KnownExceptions; the layer no longer uses them: {string.Join(", ", stale)}");
        }

        [Fact]
        public void Forbidden_CoversEveryLayer()
            => Assert.Equal(Layers.Keys.Order(), Forbidden.Keys.Order());

        [Fact]
        public void UsedTypes_FindsAKnownUse()
        {
            // Guards the metadata reader itself: Services is known to use IUnitOfWork from Domain
            Assert.Contains(("Domain", typeof(IUnitOfWork).FullName!), UsedTypes("Services"));
        }

        private static bool IsEnum(string assembly, string type)
            => Layers[assembly].GetType(type)?.IsEnum == true;

        // Every type the layer's code refers to in another assembly, read from the metadata's TypeRef table.
        // Nested types are reported with their outermost type's assembly and a "+" name, as Type.GetType expects
        private static HashSet<(string Assembly, string Type)> UsedTypes(string layer)
        {
            using var pe = new PEReader(File.OpenRead(Layers[layer].Location));
            var reader = pe.GetMetadataReader();
            var used = new HashSet<(string, string)>();

            foreach (var handle in reader.TypeReferences)
            {
                var type = reader.GetTypeReference(handle);
                var name = reader.GetString(type.Name);

                while (type.ResolutionScope.Kind == HandleKind.TypeReference)
                {
                    type = reader.GetTypeReference((TypeReferenceHandle)type.ResolutionScope);
                    name = $"{reader.GetString(type.Name)}+{name}";
                }

                if (type.ResolutionScope.Kind != HandleKind.AssemblyReference)
                    continue;

                var ns = reader.GetString(type.Namespace);
                var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
                used.Add((reader.GetString(assembly.Name), ns.Length == 0 ? name : $"{ns}.{name}"));
            }

            return used;
        }
    }
}
