using Shared.Authorization;

namespace ClinicFlow.Tests.Authorization
{
    // The module catalog and effective modules (docs/v2/adr/0008 decisions 3–6). The frontend's modules.ts computes
    // the same (effectiveModules, requiredBy), so the two sides agree on what a plan unlocks
    public class ModulesTests
    {
        [Fact]
        public void Catalog_IsTheCoreThenTheAddOns()
        {
            Assert.Equal(["ORG", "STF", "ACC", "SVC", "SCH", "APT", "NTF", "AUD"], Modules.Catalog.Take(8));
            Assert.Equal(["FD", "BIL", "EMR", "INS", "RPT", "PAT", "REC", "BOT"], Modules.Catalog.Skip(8));
            Assert.Equal(Modules.Catalog.Take(8).Order(), Modules.Core.Order());
            Assert.Equal(Modules.Catalog.Skip(8).Order(), Modules.AddOns.Order());
            Assert.Empty(Modules.Core.Intersect(Modules.AddOns));
        }

        [Fact]
        public void Dependencies_AreBetweenAddOns()
        {
            Assert.All(Modules.Dependencies, d =>
            {
                Assert.Contains(d.Key, Modules.AddOns);
                Assert.All(d.Value, needed => Assert.Contains(needed, Modules.AddOns));
            });
        }

        [Fact]
        public void Effective_AlwaysHasTheCore_PlusThePlansAddOns()
        {
            var modules = Modules.Effective([Modules.FrontDesk, Modules.Billing], []);

            Assert.Superset(Modules.Core.ToHashSet(), modules.ToHashSet());
            Assert.Contains(Modules.FrontDesk, modules);
            Assert.Contains(Modules.Billing, modules);
            Assert.DoesNotContain(Modules.ClinicalRecords, modules);
        }

        [Fact]
        public void Effective_AppliesExtrasAndAddOnsSwitchedOff()
        {
            var modules = Modules.Effective([Modules.FrontDesk, Modules.Billing], [(Modules.Recruitment, true), (Modules.FrontDesk, false)]);

            Assert.Contains(Modules.Recruitment, modules);
            Assert.DoesNotContain(Modules.FrontDesk, modules);
            Assert.Contains(Modules.Billing, modules);
        }

        [Fact]
        public void Effective_SwitchesOnWhatAnEnabledAddOnNeeds()
        {
            var modules = Modules.Effective([], [(Modules.Insurance, true), (Modules.Chatbot, true)]);

            Assert.Superset(
                new HashSet<string> { Modules.Insurance, Modules.Billing, Modules.ClinicalRecords, Modules.Chatbot, Modules.PatientPortal },
                modules.ToHashSet());
        }

        [Fact]
        public void Effective_ADependencySwitchedOff_ComesBackWhileItIsNeeded()
        {
            // Such an override is refused where overrides are written; the closure runs last either way
            var modules = Modules.Effective([Modules.Insurance], [(Modules.Billing, false)]);

            Assert.Contains(Modules.Billing, modules);
        }

        [Fact]
        public void Effective_CoreCantBeSwitchedOff_AndUnknownCodesAreDropped()
        {
            var modules = Modules.Effective(["XYZ"], [(Modules.Appointments, false), ("ABC", true)]);

            Assert.Contains(Modules.Appointments, modules);
            Assert.DoesNotContain("XYZ", modules);
            Assert.DoesNotContain("ABC", modules);
        }

        [Fact]
        public void RequiredBy_NamesTheEnabledAddOnsThatNeedTheModule()
        {
            Assert.Equal([Modules.Insurance], Modules.RequiredBy(Modules.Billing, Modules.Effective([Modules.Insurance], [])));
            Assert.Empty(Modules.RequiredBy(Modules.Billing, Modules.Effective([Modules.Billing], [])));
            Assert.Equal([Modules.Chatbot], Modules.RequiredBy(Modules.PatientPortal, Modules.Effective([Modules.Chatbot], [])));
        }

        [Fact]
        public void InCatalogOrder_SortsAndDropsUnknownCodes()
            => Assert.Equal([Modules.Organization, Modules.FrontDesk, Modules.Chatbot],
                Modules.InCatalogOrder([Modules.Chatbot, "XYZ", Modules.FrontDesk, Modules.Organization]));

        [Fact]
        public void IsCore_OnlyForCoreModules()
        {
            Assert.True(Modules.IsCore(Modules.Appointments));
            Assert.False(Modules.IsCore(Modules.FrontDesk));
        }
    }
}
