using KSP.Localization;
using SystemHeat;
using TMPro;

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
    /// The resource that is being used as a coolant.
    /// </summary>
    [KSPField]
    public string coolantName;

    /// <summary>
    /// The coolant resource definition.
    /// </summary>
    public PartResourceDefinition coolant;

    /// <summary>
    /// A curve with the total specific heat capacity values in (kJ/t/K) to use
    /// at different temperatures. The value picked here will be based on
    /// the exhaust temperature.
    ///
    /// This is meant to cover the total cost of bringing the heat up from the
    /// coolant temperature, not just exact specific heat capacity at that
    /// temperature.
    /// </summary>
    [KSPField]
    public FloatCurve coolantSpecificHeatCapacityCurve = new();

    /// <summary>
    /// The amount of energy it takes to vaporize the coolant in kJ/t.
    /// </summary>
    [KSPField]
    public double coolantVaporizationCost = 0d;

    /// <summary>
    /// How much coolant is being ejected each second (in t/s) when this
    /// radiator is active.
    /// </summary>
    [KSPField]
    public double coolantMassFlow = 1d;

    /// <summary>
    /// The flow mode to use when draining coolant.
    /// </summary>
    [KSPField]
    public ResourceFlowMode coolantFlowMode = ResourceFlowMode.NULL;

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
    /// When just above the nominal temperature this radiator will scale down
    /// resource usage. This field indicates how wide that range should be.
    /// </summary>
    [KSPField]
    public double flowScaleRange = 25d;

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
        return Localizer.Format(
            "#LOC_SHX_ModuleSHXOpenCycleRadiator_PartInfo",
            CalculateFluxAt(500.0).ToString("F2"),
            CalculateFluxAt(1000.0).ToString("F2"),
            CalculateFluxAt(2000.0).ToString("F2"),
            CalculateFluxAt(3000.0).ToString("F2"),
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
            if (HighLogic.LoadedSceneIsEditor)
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

        if (HeatModule is null || coolant is null)
            return;

        HeatModule.AddFlux(moduleID, (float)coolantTemperature, 0f, useForNominal: false);

        if (!IsCooling)
            return;
        if (HeatModule.LoopTemperature < minOperatingTemperature)
            return;
        if (HeatModule.LoopTemperature > maxOperatingTemperature && !HighLogic.LoadedSceneIsEditor)
            return;

        var loopTemp = HeatModule.LoopTemperature;
        var nominalTemp = HeatModule.Loop.NominalTemperature;
        if (loopTemp < nominalTemp)
            return;

        var efficiency = heatTransferEfficiencyCurve.Evaluate(HeatModule.LoopTemperature);
        var coolantTemp = GetCoolantTemperature();
        ExhaustTemperature = UtilMath.LerpUnclamped(
            coolantTemp,
            HeatModule.LoopTemperature,
            efficiency
        );

        var diff = ExhaustTemperature - coolantTemp;
        var shc = (double)coolantSpecificHeatCapacityCurve.Evaluate((float)ExhaustTemperature);

        // Scale coolant usage down when just above the loop nominal temperature.
        var mult = 1.0;
        if (loopTemp < nominalTemp + flowScaleRange)
            mult = (loopTemp - nominalTemp) / flowScaleRange;

        double rate;
        double density = GetCoolantDensity();
        double requested = coolantMassFlow * mult * (flowLimitPercentage * 0.01);
        if (HighLogic.LoadedSceneIsFlight)
        {
            var amount = part.RequestResource(
                coolant.id,
                requested * TimeWarp.fixedDeltaTime / density,
                coolantFlowMode
            );

            rate = amount * density / TimeWarp.fixedDeltaTime;
        }
        else
        {
            rate = requested;
        }

        var flux = (diff * shc + coolantVaporizationCost) * rate;
        HeatModule.AddFlux(moduleID, (float)coolantTemp, -(float)flux, useForNominal: false);
        CoolantFlowFraction = rate / coolantMassFlow;
    }

    public virtual void Update()
    {
        if (!part.IsPAWVisible())
            return;

        var density = GetCoolantDensity();
        var flow = CoolantFlowFraction * coolantMassFlow / density;
        var flowUI = flow < 0.1 ? flow.ToString("G8") : flow.ToString("F2");
        CoolantFlowUI = Localizer.Format(
            "#LOC_SHX_ModuleSHXOpenCycleRadiator_CoolantFlow_Fmt",
            flowUI,
            coolant.abbreviation
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

    double CalculateFluxAt(double temp)
    {
        var efficiency = heatTransferEfficiencyCurve.Evaluate((float)temp);
        var exhaustTemp = UtilMath.LerpUnclamped(coolantTemperature, temp, efficiency);
        var shc = coolantSpecificHeatCapacityCurve.Evaluate((float)exhaustTemp);

        var diff = coolantTemperature - temp;
        var rate = coolantMassFlow;
        var flux = (diff * shc + coolantVaporizationCost) * rate;

        return flux;
    }

    double GetCoolantDensity()
    {
        if (ignoreCoolantDensity)
            return 1.0;
        if (coolant is null)
            return 1.0;
        if (coolant.density == 0.0)
            return 1.0;

        return coolant.density;
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        if (coolantName != null)
            coolant = PartResourceLibrary.Instance?.resourceDefinitions?[coolantName];
    }

    static FloatCurve DefaultEfficiencyCurve()
    {
        FloatCurve curve = new();
        curve.Add(0f, 1f);
        return curve;
    }
}
