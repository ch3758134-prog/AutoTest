using System.IO;
using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class StationDeviceConfigurationTests
{
    [Fact]
    public void NewDeviceSettingsContainOnlyFixtureContract()
    {
        var config = StationDeviceConfiguration.New();
        config.Validate();
        Assert.Equal(3, config.SchemaVersion);
        Assert.False(config.Fixture.AutoClampEnabled);
        Assert.Empty(typeof(StationDeviceConfiguration).GetProperties()
            .Where(p => p.Name is "Mode" or "Products" or "IdentityRows"
                or "Rules" or "FixedProjectId"));
    }

    [Fact]
    public void OldMixedProductionConfigurationIsRejected()
    {
        var obsolete = StationDeviceConfiguration.New() with { SchemaVersion = 2 };
        Assert.Throws<InvalidDataException>(() => obsolete.Validate());
    }

    [Fact]
    public void MissingActuatorSelectionCannotEnableAutomaticFixture()
    {
        var device = StationDeviceConfiguration.New() with
        {
            Fixture = ProductionFixtureConfiguration.Draft with
            {
                AutoClampEnabled = true, UseClamp1 = false, UseClamp2 = false
            }
        };
        device.Validate(); // Draft can be saved without energizing an output.
        Assert.Throws<InvalidDataException>(() =>
            StationFixtureExecutionPlan.From(device.Fixture));
    }

    [Fact]
    public void StationDeviceSetupCannotClaimPhysicalProof()
    {
        var config = StationDeviceConfiguration.New();
        Assert.Contains(config.Fixture.PhysicalBlockers(),
            x => x.Contains("START", StringComparison.Ordinal));
    }
}