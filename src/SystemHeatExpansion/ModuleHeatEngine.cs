using System;
using KSP.Localization;
using SystemHeat;
using SystemHeatExpansion.Utils;
using UnityEngine;

namespace SystemHeatExpansion;

/// <summary>
/// A generator that generates power from the temperature differential between
/// two SystemHeat loops.
/// </summary>
[KSPModule("#LOC_SHX_ModuleSHXHeatEngine_ModuleName")]
public class ModuleSHXHeatEngine : PartModule
{
    #region SystemHeat Fields
    /// <summary>
    /// A unique identifier for this module on the current part.
    /// </summary>
    [KSPField]
    public string moduleID = "ModuleSHXHeatEngine";

    /// <summary>
    /// This should correspond to the related ModuleSystemHeat. If not specified,
    /// the first found module will be used.
    /// </summary>
    [KSPField]
    public string systemHeatModuleID1;

    /// <summary>
    /// This should correspond to the related ModuleSystemHeat. If not specified,
    /// the first found module will be used.
    /// </summary>
    [KSPField]
    public string systemHeatModuleID2;

    public ModuleSystemHeat HeatModule1;
    public ModuleSystemHeat HeatModule2;
    #endregion

    #region Config Fields
    /// <summary>
    /// What is the efficiency of this engine in relation to the Carnot
    /// efficiency? 1 means it works at the carnot efficiency, 0 means it extracts
    /// no energy from the temperature differential at all.
    /// </summary>
    [KSPField]
    public double efficiency = 1.0;

    /// <summary>
    /// How much heat is this generator capable of moving at full-blast in kW.
    /// </summary>
    [KSPField]
    public double heatRate = 1.0;

    /// <summary>
    /// The minimum temperature that this generator can output to.
    /// </summary>
    [KSPField]
    public float minOutletTemperature = 0f;

    /// <summary>
    /// The maximum temperature that this generator can consume from.
    /// </summary>
    [KSPField]
    public float maxInletTemperature = 4000f;

    /// <summary>
    /// The minimum difference between the inlet and outlet temperatures in
    /// order for the generator to work.
    /// </summary>
    [KSPField]
    public float minOperatingDifference = 100f;

    /// <summary>
    /// If the temperature difference is less than this value we'll scale down
    /// the amount of power that we are transferring. This prevents flickering
    /// in the reactor PAW.
    /// </summary>
    [KSPField]
    public double hysterisisRange = 25.0;
    #endregion

    #region UI Fields
    [KSPField(
        isPersistant = true,
        guiActive = true,
        guiActiveEditor = false,
        guiName = "#LOC_SHX_ModuleSHXHeatEngine_Direction",
        groupName = "ModuleSHXHeatEngine",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatEngine_GroupDisplayName",
        groupStartCollapsed = false
    )]
    [UI_Toggle(enabledText = "", disabledText = "", affectSymCounterparts = UI_Scene.All)]
    public bool ToggleSource;

    [KSPField(
        isPersistant = true,
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXHeatEngine_OutletTemperature",
        groupName = "ModuleSHXHeatEngine",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatEngine_GroupDisplayName",
        groupStartCollapsed = false
    )]
    [UI_FloatRange(minValue = 0f, maxValue = 5000f)]
    public float OutletTemperature = 0f;

    [KSPField(
        isPersistant = true,
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXHeatEngine_Limiter",
        groupName = "ModuleSHXHeatEngine",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatEngine_GroupDisplayName",
        groupStartCollapsed = false
    )]
    [UI_FloatRange(minValue = 0f, maxValue = 100f)]
    public float LimitPercentage = 100f;

    [KSPField(
        isPersistant = false,
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXHeatEngine_GenerationRate",
        groupName = "ModuleSHXHeatEngine",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatEngine_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public string GenerationRateUI = "-";
    #endregion

    #region State
    [KSPField(isPersistant = true)]
    public bool Enabled = true;

    [KSPField(isPersistant = true)]
    public double ActivationFraction = 1.0;

    public double CurrentPower = 0.0;
    #endregion

    #region Events
    [KSPEvent(
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXHeatEngine_Action_Activate",
        groupName = "ModuleSHXHeatEngine",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatEngine_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public void Enable()
    {
        Enabled = true;
        UpdateStatus();
    }

    [KSPEvent(
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXHeatEngine_Action_Deactivate",
        groupName = "ModuleSHXHeatEngine",
        groupDisplayName = "#LOC_SHX_ModuleSHXHeatEngine_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public void Disable()
    {
        Enabled = false;
        UpdateStatus();
    }

    public void Toggle()
    {
        if (Enabled)
            Disable();
        else
            Enable();
    }
    #endregion

    #region Actions
    [KSPAction("#LOC_SHX_ModuleSHXHeatEngine_Action_Activate")]
    public virtual void ActivateAction(KSPActionParam _) => Enable();

    [KSPAction("#LOC_SHX_ModuleSHXHeatEngine_Action_Deactivate")]
    public virtual void DeactivateAction(KSPActionParam _) => Disable();

    [KSPAction("#LOC_SHX_ModuleSHXHeatEngine_Action_Toggle")]
    public virtual void ToggleAction(KSPActionParam _) => Toggle();
    #endregion

    public ModuleSystemHeat SrcModule => ToggleSource ? HeatModule2 : HeatModule1;
    public ModuleSystemHeat DstModule => ToggleSource ? HeatModule1 : HeatModule2;
    public float Limit => LimitPercentage * 0.01f;

    protected virtual void UpdateStatus()
    {
        bool changed = Events["Enable"].active == Enabled;
        Events["Enable"].active = !Enabled;
        Events["Disable"].active = Enabled;

        if (changed)
        {
            MonoUtilities.RefreshPartContextWindow(part);

            if (!Enabled)
                ClearFlux();
        }
    }

    public override string GetModuleDisplayName()
    {
        return Localizer.GetStringByTag("#LOC_SHX_ModuleSHXHeatEngine_GroupDisplayName");
    }

    public override string GetInfo()
    {
        return Localizer.Format("#LOC_SHX_ModuleSHXHeatEngine_PartInfo");
    }

    public override void OnAwake()
    {
        base.OnAwake();

        resHandler.moduleResourceBasedPrimaryIsInput = false;
    }

    public override void OnStart(StartState state)
    {
        HeatModule1 = ModuleUtils.FindHeatModule(part, systemHeatModuleID1);
        HeatModule2 = ModuleUtils.FindHeatModule(part, systemHeatModuleID2);

        var toggle = HighLogic.LoadedSceneIsEditor
            ? (UI_Toggle)Fields[nameof(ToggleSource)].uiControlEditor
            : (UI_Toggle)Fields[nameof(ToggleSource)].uiControlFlight;
        toggle.onFieldChanged = (a, b) => OnToggleDirection();
        OnToggleDirection();

        var range = HighLogic.LoadedSceneIsEditor
            ? (UI_FloatRange)Fields[nameof(OutletTemperature)].uiControlEditor
            : (UI_FloatRange)Fields[nameof(OutletTemperature)].uiControlFlight;

        range?.minValue = minOutletTemperature;
        range?.maxValue = maxInletTemperature;
        OutletTemperature = Mathf.Clamp(
            OutletTemperature,
            minOutletTemperature,
            maxInletTemperature
        );

        UpdateStatus();
    }

    public virtual void FixedUpdate()
    {
        CurrentPower = 0.0;

        if (HeatModule1 is null || HeatModule2 is null)
            return;

        if (!Enabled)
            return;

        var outletTemperature = Mathf.Max(DstModule.LoopTemperature, OutletTemperature);
        var inletTemperature = SrcModule.LoopTemperature;

        if (inletTemperature > maxInletTemperature && !HighLogic.LoadedSceneIsEditor)
        {
            ClearFlux();
            return;
        }

        if (outletTemperature + minOperatingDifference >= inletTemperature)
        {
            ClearFlux();
            return;
        }

        var mult = UtilMath.Clamp01(
            (inletTemperature - outletTemperature - minOperatingDifference) / hysterisisRange
        );
        var carnotEff = 1.0 - outletTemperature / inletTemperature;
        var rate = heatRate * Limit * mult;
        var power = Math.Abs(SrcModule.consumedSystemFlux) * carnotEff * efficiency;

        // Add flux to the dst loop based on how much we actually consumed in the
        // last frame.
        DstModule.AddFlux(
            moduleID,
            outletTemperature,
            Math.Abs(SrcModule.consumedSystemFlux),
            useForNominal: DstModule.nominalLoopTemperature <= OutletTemperature
        );
        SrcModule.AddFlux(moduleID, inletTemperature, (float)-rate, useForNominal: false);

        CurrentPower = power;

        if (HighLogic.LoadedSceneIsEditor)
            return;

        resHandler.UpdateModuleResourceOutputs(power);
    }

    public virtual void Update()
    {
        if (!part.IsPAWVisible())
            return;

        if (resHandler.outputResources.Count == 0)
        {
            GenerationRateUI = "-";
            return;
        }

        var resource = resHandler.outputResources[0];
        var rate = resource.rate * CurrentPower;
        var abbrev = resource.resourceDef.abbreviation;

        GenerationRateUI = Localizer.Format(
            "#LOC_SHX_ModuleSHXHeatEngine_GenerationRate_Fmt",
            rate.ToString("F2"),
            abbrev
        );
    }

    void OnToggleDirection()
    {
        SrcModule.ignoreTemperature = true;
        DstModule.ignoreTemperature = false;

        var toggle = HighLogic.LoadedSceneIsEditor
            ? (UI_Toggle)Fields[nameof(ToggleSource)].uiControlEditor
            : (UI_Toggle)Fields[nameof(ToggleSource)].uiControlFlight;

        var text = Localizer.Format(
            "#LOC_SHX_ModuleSHXHeatEngine_Direction_Fmt",
            SrcModule.LoopID,
            DstModule.LoopID
        );
        toggle.enabledText = text;
        toggle.disabledText = text;
    }

    void ClearFlux()
    {
        SrcModule?.AddFlux(moduleID, 0f, 0f, useForNominal: false);
        DstModule?.AddFlux(moduleID, 0f, OutletTemperature, useForNominal: Enabled);
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);
        resHandler.inputResources.Clear();
    }
}
