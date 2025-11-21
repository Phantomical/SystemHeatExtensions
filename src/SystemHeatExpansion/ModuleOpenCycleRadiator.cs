using System;
using KSP.Localization;
using SystemHeat;
using SystemHeatExpansion.Utils;

namespace SystemHeatExpansion;

/// <summary>
/// A module that removes heat by heating up a coolant and dumping it overboard.
/// </summary>
[KSPModule("#LOC_SHX_ModuleSHXOpenCycleRadiator_ModuleName")]
public class ModuleSHXOpenCycleRadiator : PartModule
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
    /// The specific enthalpy of coolant at each given temperature.
    ///
    /// This is basically just meant to be the integral of the specific heat
    /// capacity, starting at absolute 0. The actual value is never used, only
    /// the differences between values at different temperatures.
    /// </summary>
    [KSPField]
    public FloatCurve coolantSpecificEnthalpyCurve = DefaultSpecificEnthalpyCurve();

    /// <summary>
    /// The starting temperature of the coolant.
    /// </summary>
    [KSPField]
    public double coolantTemperature = 0d;

    /// <summary>
    /// Specifies that the coolant temperature is the outside temperature
    /// instead of a specific fixed temperature.
    /// </summary>
    [KSPField]
    public bool coolantIsAtmosphereTemperature = false;

    /// <summary>
    /// The minimum operating temperature of this radiator. If the loop
    /// temperature is below this it won't do anything.
    /// </summary>
    [KSPField]
    public double minOperatingTemperature = 350d;

    /// <summary>
    /// The maximum operating temperature of this radiator. If the loop
    /// temperature is above this then it won't do anything.
    /// </summary>
    [KSPField]
    public double maxOperatingTemperature = 4000d;

    /// <summary>
    /// What fraction of the heat difference gets transferred to the coolant.
    /// 1 means that the coolent is emitted at the same temperature as the heat
    /// loop, 0 means no heat is transferred.
    /// </summary>
    [KSPField]
    public FloatCurve heatTransferEfficiencyCurve = DefaultEfficiencyCurve();

    /// <summary>
    /// Ignore the density of the coolant and instead treat all coolant values
    /// as being in terms of resource units, instead of mass. If the coolant
    /// is massless (density = 0) then this is automatically applied.
    /// </summary>
    [KSPField]
    public bool ignoreCoolantDensity = false;
    #endregion

    #region State Fields
    /// <summary>
    /// Is this module enabled?
    /// </summary>
    [KSPField(isPersistant = true)]
    public bool IsCooling = false;

    /// <summary>
    /// The exhaust temperature of the coolant as it leaves the readiator.
    /// This is meant to be used by waterfall controllers.
    /// </summary>
    [KSPField]
    public double ExhaustTemperature;

    /// <summary>
    /// How much coolant is being ejected? This is meant to be used by
    /// waterfall controllers.
    /// </summary>
    [KSPField]
    public double CoolantFlowFraction;
    #endregion

    #region UI Fields
    [KSPField(
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXOpenCycleRadiator_CoolantFlow",
        groupName = "ModuleSHXOpenCycleRadiator",
        groupDisplayName = "#LOC_SHX_ModuleSHXOpenCycleRadiator_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public string CoolantFlowUI = "-";

    /// <summary>
    /// A limiter on how much of the max flow rate can be used.
    /// </summary>
    [KSPField(
        isPersistant = true,
        guiActive = true,
        guiActiveEditor = true,
        guiActiveUnfocused = true,
        guiName = "#LOC_SHX_ModuleSHXOpenCycleRadiator_FlowLimit",
        groupName = "ModuleSHXOpenCycleRadiator",
        groupDisplayName = "#LOC_SHX_ModuleSHXOpenCycleRadiator_GroupDisplayName",
        groupStartCollapsed = false
    )]
    [UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 5f)]
    public float flowLimitPercentage = 100f;
    #endregion

    #region Events
    [KSPEvent(
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXOpenCycleRadiator_Action_Activate",
        groupName = "ModuleSHXOpenCycleRadiator",
        groupDisplayName = "#LOC_SHX_ModuleSHXOpenCycleRadiator_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public void Enable()
    {
        IsCooling = true;
        UpdateStatus();
    }

    [KSPEvent(
        guiActive = true,
        guiActiveEditor = true,
        guiName = "#LOC_SHX_ModuleSHXOpenCycleRadiator_Action_Deactivate",
        groupName = "ModuleSHXOpenCycleRadiator",
        groupDisplayName = "#LOC_SHX_ModuleSHXOpenCycleRadiator_GroupDisplayName",
        groupStartCollapsed = false
    )]
    public void Disable()
    {
        IsCooling = false;
        UpdateStatus();
    }

    public void Toggle()
    {
        if (IsCooling)
            Disable();
        else
            Enable();
    }
    #endregion

    #region Actions
    [KSPAction("#LOC_SHX_ModuleSHXOpenCycleRadiator_Action_Activate")]
    public virtual void ActivateAction(KSPActionParam _) => Enable();

    [KSPAction("#LOC_SHX_ModuleSHXOpenCycleRadiator_Action_Deactivate")]
    public virtual void DeactivateAction(KSPActionParam _) => Disable();

    [KSPAction("#LOC_SHX_ModuleSHXOpenCycleRadiator_Action_Toggle")]
    public virtual void ToggleAction(KSPActionParam _) => Toggle();

    public void ApplyActivation(KSPActionType action)
    {
        switch (action)
        {
            case KSPActionType.Activate:
                Enable();
                break;
            case KSPActionType.Deactivate:
                Disable();
                break;
            case KSPActionType.Toggle:
                Toggle();
                break;
        }
    }
    #endregion

    public override string GetModuleDisplayName()
    {
        return Localizer.GetStringByTag("#LOC_SHX_ModuleSHXOpenCycleRadiator_GroupDisplayName");
    }

    public override string GetInfo()
    {
        double coolantMassFlow = GetRequestedCoolantRate();
        return Localizer.Format(
            "#LOC_SHX_ModuleSHXOpenCycleRadiator_PartInfo",
            GetFluxAt(500.0, coolantMassFlow).ToString("F2"),
            GetFluxAt(1000.0, coolantMassFlow).ToString("F2"),
            GetFluxAt(2000.0, coolantMassFlow).ToString("F2"),
            GetFluxAt(3000.0, coolantMassFlow).ToString("F2"),
            minOperatingTemperature.ToString("F0"),
            maxOperatingTemperature.ToString("F0")
        );
    }

    public override void OnStart(StartState state)
    {
        HeatModule ??= ModuleUtils.FindHeatModule(part, systemHeatModuleID);

        UpdateStatus();
    }

    public virtual double GetCoolantTemperature()
    {
        if (coolantIsAtmosphereTemperature)
        {
            if (part?.partInfo?.partPrefab)
            {
                // If we're currently in part compilation then just return 0C.
                return 273d;
            }
            else if (HighLogic.LoadedSceneIsEditor)
            {
                return vessel
                        ?.FindVesselModuleImplementing<SystemHeatVessel>()
                        ?.Simulator?.AtmoSim?.ExternalTemperature ?? 3d;
            }
            else
            {
                return vessel.atmosphericTemperature;
            }
        }

        return coolantTemperature;
    }

    public virtual void FixedUpdate()
    {
        ExhaustTemperature = 0.0;
        CoolantFlowFraction = 0.0;

        if (HeatModule is null)
            return;

        HeatModule.AddFlux(moduleID, (float)coolantTemperature, 0f, useForNominal: false);

        if (!IsCooling)
            return;
        if (HeatModule.LoopTemperature < minOperatingTemperature)
            return;
        if (HeatModule.LoopTemperature > maxOperatingTemperature && !HighLogic.LoadedSceneIsEditor)
            return;
        if (HeatModule.LoopTemperature < HeatModule.Loop.NominalTemperature)
            return;

        var efficiency = heatTransferEfficiencyCurve.Evaluate(HeatModule.LoopTemperature);
        var coolantTemp = GetCoolantTemperature();
        var exhaustTemp = UtilMath.LerpUnclamped(
            coolantTemp,
            HeatModule.LoopTemperature,
            efficiency
        );

        // How much energy does it take to heat the coolant up to the exhaust temp?
        var energy =
            coolantSpecificEnthalpyCurve.Evaluate((float)exhaustTemp)
            - coolantSpecificEnthalpyCurve.Evaluate((float)coolantTemp);

        ExhaustTemperature = exhaustTemp;

        // Scale coolant usage down when just above the loop nominal temperature.
        var dt = HighLogic.LoadedSceneIsEditor ? HeatModule.Loop.timeStep : TimeWarp.fixedDeltaTime;

        double requestedFlux = GetRequestedCoolantRate() * energy;
        double satisfaction = 1.0;
        if (!HighLogic.LoadedSceneIsEditor)
        {
            string error = null;
            double consumedFlux = HeatModule.GetConsumedModuleFlux(moduleID);
            if (consumedFlux > 0.0)
            {
                satisfaction = resHandler.UpdateModuleResourceInputs(
                    ref error,
                    useFlowMode: true,
                    rateMultiplier: consumedFlux / requestedFlux,
                    threshold: 0.0,
                    returnOnFirstLack: false,
                    average: true,
                    stringOps: true
                );
            }
        }

        double frac = flowLimitPercentage * 0.01 * Math.Max(satisfaction, 1.0);
        double flux = requestedFlux * frac;
        HeatModule.AddFlux(
            moduleID,
            (float)coolantTemp,
            -(float)flux,
            useForNominal: HeatModule.Loop.NominalTemperature
                <= Math.Max(minOperatingTemperature, coolantTemp)
        );
        CoolantFlowFraction = frac;
    }

    public virtual void Update()
    {
        if (!part.IsPAWVisible())
            return;

        var flow = CoolantFlowFraction * GetRequestedCoolantRate();
        var flowUI = flow < 0.1 ? flow.ToString("G8") : flow.ToString("F2");

        string abbreviation =
            resHandler.inputResources.Count == 1
                ? resHandler.inputResources[0].resourceDef.abbreviation
                : Localizer.Format("#LOC_SHX_ModuleSHXOpenCycleRadiator_Unit_Units");

        CoolantFlowUI = Localizer.Format(
            "#LOC_SHX_ModuleSHXOpenCycleRadiator_CoolantFlow_Fmt",
            flowUI,
            abbreviation
        );
    }

    protected virtual void UpdateStatus()
    {
        bool changed = Events["Enable"].active != IsCooling;
        Events["Enable"].active = !IsCooling;
        Events["Disable"].active = IsCooling;

        if (changed)
            MonoUtilities.RefreshPartContextWindow(part);
    }

    double GetRequestedCoolantRate()
    {
        double mass = 0.0;
        if (ignoreCoolantDensity)
        {
            foreach (var input in resHandler.inputResources)
                mass += input.rate;
        }
        else
        {
            foreach (var input in resHandler.inputResources)
                mass += input.rate * input.resourceDef.density;
        }

        return mass;
    }

    double GetFluxAt(double temp, double massFlow)
    {
        var coolantTemp = GetCoolantTemperature();
        var efficiency = heatTransferEfficiencyCurve.Evaluate((float)temp);
        var exhaustTemp = UtilMath.LerpUnclamped(coolantTemperature, temp, efficiency);

        // How much energy does it take to heat the coolant up to the exhaust temp?
        var energy =
            coolantSpecificEnthalpyCurve.Evaluate((float)exhaustTemp)
            - coolantSpecificEnthalpyCurve.Evaluate((float)coolantTemp);

        return energy * massFlow;
    }

    static FloatCurve DefaultEfficiencyCurve()
    {
        FloatCurve curve = new();
        curve.Add(0f, 1f);
        return curve;
    }

    static FloatCurve DefaultSpecificEnthalpyCurve()
    {
        FloatCurve curve = new();
        curve.Add(0f, 0f);
        // 1 kJ/kg all the way up to 6000K
        curve.Add(6000f, 6000f * 1000f);
        return curve;
    }
}
