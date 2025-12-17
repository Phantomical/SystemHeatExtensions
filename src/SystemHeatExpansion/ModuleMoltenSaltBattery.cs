using KSP.Localization;
using SystemHeat;

namespace SystemHeatExpansion;

/// <summary>
/// A battery that only works when
/// </summary>
[KSPModule("#LOC_SHX_ModuleSHXMoltenSaltBattery_ModuleName")]
public class ModuleSHXMoltenSaltBattery : PartModule
{
    #region SystemHeat Fields
    public ModuleSystemHeat HeatModule;

    /// <summary>
    /// A unique identifier for this module on the current part.
    /// </summary>
    [KSPField]
    public string moduleID = "ModuleSHXOpenCycleRadiator";

    /// <summary>
    /// This should correspond to the related ModuleSystemHeat. If not specified,
    /// the first found module will be used.
    /// </summary>
    [KSPField]
    public string systemHeatModuleID;
    #endregion

    #region Config Fields
    /// <summary>
    /// The minimum temperature that this battery can work at.
    /// </summary>
    [KSPField]
    public double minOperatingTemperature = 0d;

    /// <summary>
    /// The maximum temperature that this battery can work at.
    /// </summary>
    [KSPField]
    public double maxOperatingTemperature = 4000d;

    /// <summary>
    /// What nominal temperature should this battery set for the loop?
    /// </summary>
    [KSPField]
    public double nominalTemperature = 0d;

    /// <summary>
    /// Can this battery break
    /// </summary>
    [KSPField]
    public bool isBreakable = false;

    /// <summary>
    /// What resource is this battery storing? There should be a `RESOURCE`
    /// node present on this part for the battery to use.
    /// </summary>
    [KSPField]
    public string chargeResourceName = "ElectricCharge";

    /// <summary>
    /// Override the group name used in the part action window for this module.
    /// </summary>
    [KSPField]
    public string pawGroupName = null;
    #endregion

    #region State
    [KSPField(
        guiActive = true,
        guiActiveEditor = true,
        guiActiveUnfocused = true,
        guiName = "#LOC_SHX_ModuleSHXMoltenSaltBattery_Status",
        groupName = "ModuleSHXMoltenSaltBattery",
        groupDisplayName = "#LOC_SHX_ModuleSHXMoltenSaltBattery_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public string Status = "-";

    [KSPField(isPersistant = true)]
    public bool Broken = false;
    #endregion

    public PartResource resource;

    public override string GetInfo()
    {
        var desc = Localizer.Format(
            "#LOC_SHX_ModuleSHXMoltenSaltBattery_PartInfo",
            minOperatingTemperature,
            maxOperatingTemperature
        );

        if (nominalTemperature > 0d)
            desc += Localizer.Format(
                "#LOC_SHX_ModuleSHXMoltenSaltBattery_PartInfo_NominalTemp",
                nominalTemperature
            );

        return desc;
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);
    }
}
