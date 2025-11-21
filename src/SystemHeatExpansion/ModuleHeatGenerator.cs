using KSP.Localization;
using SystemHeat;
using SystemHeatExpansion.Utils;

namespace SystemHeatExpansion;

/// <summary>
/// A space heater. Consumes resources to and adds heat to a loop.
/// </summary>
[KSPModule("#LOC_SHX_ModuleSHXHeatGenerator_ModuleName")]
public class ModuleSHXHeatGenerator : PartModule
{
    #region SystemHeat Fields
    public ModuleSystemHeat HeatModule;

    /// <summary>
    /// A unique identifier for this module on the current part.
    /// </summary>
    [KSPField]
    public string moduleID = "ModuleSHXHeater";

    /// <summary>
    /// This should correspond to the related ModuleSystemHeat. If not specified,
    /// the first found module will be used.
    /// </summary>
    [KSPField]
    public string systemHeatModuleID;
    #endregion

    #region Config Fields
    /// <summary>
    /// How much flux can this heater emit at full blast? (in kW)
    /// </summary>
    [KSPField]
    public double systemPower = 0.0;

    /// <summary>
    /// The target temperature for this heater module.
    /// </summary>
    [KSPField]
    public double targetTemperature = 293f;
    #endregion

    #region UI Fields
    [KSPField(
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXHeatGenerator_Status",
        groupName = "ModuleSHXHeatGenerator",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatGenerator_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public string Status = "-";
    #endregion

    #region State
    /// <summary>
    /// Is this module enabled?
    /// </summary>
    [KSPField(isPersistant = true)]
    public bool IsHeating = true;
    #endregion

    #region Events
    [KSPEvent(
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXHeatGenerator_Action_Activate",
        groupName = "ModuleSHXHeatGenerator",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatGenerator_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public void Enable()
    {
        IsHeating = true;
        UpdateStatus();
    }

    [KSPEvent(
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXHeatGenerator_Action_Deactivate",
        groupName = "ModuleSHXHeatGenerator",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatGenerator_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public void Disable()
    {
        IsHeating = false;
        UpdateStatus();
    }

    public void Toggle()
    {
        if (IsHeating)
            Disable();
        else
            Enable();
    }
    #endregion

    #region Actions
    [KSPAction("#LOC_SHX_ModuleSHXHeatGenerator_Action_Activate")]
    public virtual void ActivateAction(KSPActionParam _) => Enable();

    [KSPAction("#LOC_SHX_ModuleSHXHeatGenerator_Action_Deactivate")]
    public virtual void DeactivateAction(KSPActionParam _) => Disable();

    [KSPAction("#LOC_SHX_ModuleSHXHeatGenerator_Action_Toggle")]
    public virtual void ToggleAction(KSPActionParam _) => Toggle();
    #endregion

    FluxController Controller = new();

    static readonly string StatusDisabled = Localizer.Format(
        "#LOC_SHX_ModuleSHXHeatGenerator_Status_Disabled"
    );
    static readonly string StatusNominal = Localizer.Format(
        "#LOC_SHX_ModuleSHXHeatGenerator_Status_Nominal"
    );

    public override string GetInfo()
    {
        return Localizer.Format("#LOC_SHX_ModuleSHXHeatGenerator_PartInfo");
    }

    public override void OnAwake()
    {
        base.OnAwake();

        resHandler.moduleResourceBasedPrimaryIsInput = true;
    }

    public override void OnStart(StartState state)
    {
        HeatModule ??= ModuleUtils.FindHeatModule(part, systemHeatModuleID);

        Controller.IsConsumer = false;

        UpdateStatus();
    }

    public virtual void FixedUpdate()
    {
        Status = StatusDisabled;

        if (HeatModule is null)
            return;
        if (HeatModule.Loop is null)
            return;

        HeatModule.AddFlux(moduleID, 0f, 0f, useForNominal: false);

        if (!IsHeating)
            return;

        Status = StatusNominal;
        Controller.ScaleEstimate = systemPower;

        double control = 1.0;
        if (HeatModule.Loop.Temperature >= targetTemperature)
        {
            var dt = HighLogic.LoadedSceneIsEditor
                ? HeatModule.Loop.timeStep
                : TimeWarp.fixedDeltaTime;
            control = Controller.Update(HeatModule.Loop, vessel, dt);
        }

        double frac = 1.0;
        if (!HighLogic.LoadedSceneIsEditor)
        {
            frac = resHandler.UpdateModuleResourceInputs(
                ref Status,
                useFlowMode: true,
                rateMultiplier: control,
                threshold: 0.01,
                average: false,
                returnOnFirstLack: true
            );
        }

        var flux = control * systemPower * frac;
        HeatModule.AddFlux(moduleID, (float)targetTemperature, (float)flux, useForNominal: true);
    }

    protected virtual void UpdateStatus()
    {
        bool changed = Events[nameof(Enable)].active != IsHeating;
        Events[nameof(Enable)].active = !IsHeating;
        Events[nameof(Disable)].active = IsHeating;

        if (changed)
            MonoUtilities.RefreshPartContextWindow(part);
    }
}
