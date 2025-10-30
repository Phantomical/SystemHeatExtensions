using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using KSP.Localization;
using SolverEngines;
using SystemHeat;
using UnityEngine;

namespace SystemHeatExtensions;

/// <summary>
/// Thermal information about a propellant. This will be loaded from the
/// propellant config node.
/// </summary>
public struct ThermalPropellant() : IConfigNode
{
    /// <summary>
    /// The specific heat capacity of this fuel.
    ///
    /// With KSP units, this is the amount of energy it takes to raise the
    /// temperature of one ton of propellant by one Kelvin. (i.e. kJ/t/K)
    /// </summary>
    public double specificHeatCapacity = 0.0;

    /// <summary>
    /// The cost (in kJ/t) to vaporize the fuel. This causes the engine to
    /// consume extra energy from the loop without a corresponding increase
    /// in exhaust temperature.
    /// </summary>
    public double vaporizationCost = 0.0;

    /// <summary>
    /// The temperature that the propellant starts at before entering the
    /// engine, in Kelvin.
    /// </summary>
    public double temperature = 0.0;

    public ThermalPropellant(ConfigNode node)
        : this()
    {
        Load(node);
    }

    public void Load(ConfigNode node)
    {
        node.TryGetValue(nameof(specificHeatCapacity), ref specificHeatCapacity);
        node.TryGetValue(nameof(vaporizationCost), ref vaporizationCost);
        node.TryGetValue(nameof(temperature), ref temperature);
    }

    public void Save(ConfigNode node)
    {
        node.AddValue(nameof(specificHeatCapacity), specificHeatCapacity);
        node.AddValue(nameof(vaporizationCost), vaporizationCost);
        node.AddValue(nameof(temperature), temperature);
    }
}

public class ThermalEngineSolver : EngineSolver
{
    const double G = 9.80665f;

    public ModuleSystemHeat HeatModule;
    public string ModuleID;

    public FloatCurve TemperatureIspCurve;
    public FloatCurve AtmosphereCurve;
    public FloatCurve AtmCurve;
    public FloatCurve VelCurveIsp;
    public FloatCurve VelCurve;
    public FloatCurve AtmCurveIsp;
    public FloatCurve ThrottleIspCurve;
    public FloatCurve ThrottleIspCurveAtmStrength;
    public FloatCurve ThrustCurve;

    public double HeatTransferEfficiency;
    public double MinOperatingTemperature;
    public double MaxOperatingTemperature;

    public double MixtureSpecificHeatCapacity;
    public double MixtureInitialTemperature;
    public double MixtureVaporizationCost;

    public bool DisableUnderwater;
    public double MaxFuelFlow;
    public double MinFuelFlow;
    public double FlowMultCap;
    public double FlowMultCapSharpness;

    public double ExhaustTemperature;
    public double LoopFlux;

    public override void CalculatePerformance(
        double airRatio,
        double throttle,
        double flowMult,
        double ispMult
    )
    {
        ExhaustTemperature = 0.0;
        LoopFlux = 0.0;

        base.CalculatePerformance(airRatio, throttle, flowMult, ispMult);

        CalculateExhaustTemperature();
        CalculateIsp(throttle, ispMult);

        statusString = Localizer.GetStringByTag("#LOC_SHX_ThermalNozzle_Nominal");

        if (throttle <= 0.0)
        {
            running = false;
            return;
        }

        if (HeatModule.LoopTemperature < MinOperatingTemperature)
        {
            running = false;
            statusString = Localizer.GetStringByTag("#LOC_SHX_ThermalNozzle_TempTooLow");
            return;
        }

        if (HeatModule.LoopTemperature > MaxOperatingTemperature)
        {
            running = false;
            statusString = Localizer.GetStringByTag("#LOC_SHX_ThermalNozzle_TempTooHigh");
            return;
        }

        if (ffFraction <= 0.0)
        {
            statusString = Localizer.GetStringByTag("#LOC_SHX_ThermalNozzle_NoPropellants");
            return;
        }

        if (DisableUnderwater && underwater)
        {
            running = false;
            statusString = Localizer.GetStringByTag("#LOC_SHX_ThermalNozzle_Underwater");
            return;
        }

        CalculateFuelFlow(throttle, flowMult);

        if (fuelFlow < MinFuelFlow)
        {
            fuelFlow = 0.0;
            running = false;
            statusString = Localizer.GetStringByTag("#LOC_SHX_ThermalNozzle_AirflowOutsideSpecs");
            return;
        }

        CalculateThrust();
    }

    void CalculateIsp(double throttle, double ispMult)
    {
        double atm = p0 * 0.001 * PhysicsGlobals.KpaToAtmospheres;
        double isp = AtmosphereCurve.Evaluate((float)atm) * ispMult;
        isp *= GetThrottlingMult(atm, throttle);
        isp *= AtmCurveIsp?.Evaluate((float)(rho * (1.0 / 1.225))) ?? 1.0;
        isp *= VelCurveIsp?.Evaluate((float)mach) ?? 1.0;
        isp *= TemperatureIspCurve?.Evaluate((float)ExhaustTemperature) ?? 1.0;
        Isp = isp;
        SFC = 3600d / Isp;
    }

    public void CalculateFuelFlow(double throttle, double flowMult)
    {
        flowMult *= AtmCurve?.Evaluate((float)(rho * (1.0 / 1.225))) ?? 1.0;
        flowMult *= VelCurve?.Evaluate((float)mach) ?? 1.0;

        if (flowMult > FlowMultCap)
        {
            double diff = flowMult - FlowMultCap;
            flowMult = FlowMultCap + diff / (FlowMultCapSharpness + diff / FlowMultCap);
        }

        fuelFlow = Math.Max(flowMult, 1e-5) * MaxFuelFlow * throttle;
    }

    public void CalculateExhaustTemperature()
    {
        ExhaustTemperature = UtilMath.Lerp(
            MixtureInitialTemperature,
            HeatModule.LoopTemperature,
            HeatTransferEfficiency
        );
    }

    public void CalculateThrust()
    {
        thrust = Isp * G * fuelFlow;
    }

    public void CalculateLoopFlux()
    {
        var flux =
            (MixtureInitialTemperature - ExhaustTemperature)
            * fuelFlow
            * MixtureSpecificHeatCapacity;
        flux += fuelFlow * MixtureVaporizationCost;

        LoopFlux = flux;
    }

    double GetThrottlingMult(double atm, double throttle)
    {
        if (ThrottleIspCurve is null || ThrottleIspCurveAtmStrength is null)
            return 1.0;
        return UtilMath.Lerp(
            1.0,
            ThrottleIspCurve.Evaluate((float)atm),
            ThrottleIspCurveAtmStrength.Evaluate((float)throttle)
        );
    }

    public void SetPropellantInfo(
        List<Propellant> propellants,
        List<ThermalPropellant> thermalPropellants
    )
    {
        double totalMass = 0.0;
        double weightedTemp = 0.0;
        double weightedSHC = 0.0;
        double weightedVap = 0.0;

        var count = Math.Min(propellants.Count, thermalPropellants.Count);
        for (int i = 0; i < count; ++i)
        {
            var prop = propellants[i];
            var tp = thermalPropellants[i];

            var mass = prop.resourceDef.density;

            totalMass += mass;
            weightedTemp += mass * tp.temperature;
            weightedSHC += mass * tp.specificHeatCapacity;
            weightedVap += mass * tp.vaporizationCost;
        }

        if (totalMass != 0.0)
        {
            MixtureInitialTemperature = weightedTemp / totalMass;
            MixtureSpecificHeatCapacity = weightedSHC / totalMass;
            MixtureVaporizationCost = weightedVap / totalMass;
        }
        else
        {
            MixtureInitialTemperature = 0.0;
            MixtureSpecificHeatCapacity = 0.0;
            MixtureVaporizationCost = 0.0;
        }
    }
}

[HarmonyPatch]
public class ModuleThermalEngine : ModuleEnginesSolver
{
    #region SystemHeat
    /// <summary>
    /// A unique identifier for this module on this part. Defaults to engineID
    /// if not set.
    /// </summary>
    [KSPField]
    public string moduleID;

    /// <summary>
    /// This should correspond to the related ModuleSystemHeat. If not specified,
    /// the first found module will be used.
    /// </summary>
    [KSPField]
    public string systemHeatModuleID;

    /// <summary>
    /// The linked SystemHeat module.
    /// </summary>
    public ModuleSystemHeat HeatModule { get; private set; }
    #endregion

    #region Temperature Config
    /// <summary>
    /// A curve that defines how the ISP of this engine varies with the exhaust
    /// temperature. This will multiplied in when calculating the ISP.
    /// </summary>
    [KSPField]
    public FloatCurve temperatureIspCurve = DefaultTempIspCurve();

    /// <summary>
    /// How efficient the engine is at transferring heat to the propellant.
    /// A value of 1 means that the propellant is brought up to the temperature
    /// of the loop, while 0 means that the propellant remains at the same temp.
    /// </summary>
    [KSPField]
    public double heatTransferEfficiency = 1.0;

    /// <summary>
    /// The minimum temperature (in K) that this engine requires to operate. Below
    /// this temperature then engine will flame out.
    /// </summary>
    [KSPField]
    public double minOperatingTemperature = 0.0;

    /// <summary>
    /// The maximum temperature (in K) before this engine shuts itself down.
    /// </summary>
    [KSPField]
    public double maxOperatingTemperature = 4000.0;

    public List<ThermalPropellant> thermalPropellants;
    #endregion

    public ThermalEngineSolver ThermalEngineSolver => (ThermalEngineSolver)engineSolver;

    public override void CreateEngine()
    {
        moduleID ??= EngineID;
        HeatModule ??= ModuleUtils.FindHeatModule(part, systemHeatModuleID);

        var solver = new ThermalEngineSolver()
        {
            HeatModule = HeatModule,
            ModuleID = moduleID,

            HeatTransferEfficiency = heatTransferEfficiency,
            MinOperatingTemperature = minOperatingTemperature,
            MaxOperatingTemperature = maxOperatingTemperature,

            TemperatureIspCurve = temperatureIspCurve,
            AtmosphereCurve = atmosphereCurve,
            AtmCurve = useAtmCurve ? atmCurve : null,
            AtmCurveIsp = useAtmCurveIsp ? atmCurveIsp : null,
            VelCurve = useVelCurve ? velCurve : null,
            VelCurveIsp = useVelCurveIsp ? velCurveIsp : null,
            ThrottleIspCurve = useThrottleIspCurve ? throttleIspCurve : null,
            ThrottleIspCurveAtmStrength = useThrottleIspCurve ? throttleIspCurveAtmStrength : null,

            DisableUnderwater = disableUnderwater,
            MaxFuelFlow = maxFuelFlow,
            MinFuelFlow = minFuelFlow,
            FlowMultCap = flowMultCap,
            FlowMultCapSharpness = flowMultCapSharpness,
        };

        solver.SetPropellantInfo(propellants, thermalPropellants);

        engineSolver = solver;
    }

    public override void OnAwake()
    {
        base.OnAwake();

        if (maxEngineTemp == 0.0)
            maxEngineTemp = maxOperatingTemperature;
    }

    public override void OnStart(StartState state)
    {
        // OnLoad doesn't run in the editor so we need to do this here.
        if (state == StartState.Editor)
            thermalPropellants ??= LoadThermalPropellantsFromPrefab();

        base.OnStart(state);
    }

    public override void FixedUpdate()
    {
        CreateEngineIfNecessary();

        if (HeatModule is null)
            return;

        var solver = ThermalEngineSolver;

        if (HighLogic.LoadedSceneIsFlight)
        {
            base.FixedUpdate();
        }
        else if (HighLogic.LoadedSceneIsEditor)
        {
            if (HeatModule.LoopTemperature < solver.MinOperatingTemperature)
                return;

            solver.fuelFlow = solver.MaxFuelFlow;
            solver.CalculateExhaustTemperature();
            solver.running = solver.LoopFlux != 0.0 && solver.ExhaustTemperature != 0.0;
        }

        solver.CalculateLoopFlux();
        HeatModule.AddFlux(
            moduleID,
            (float)Math.Max(solver.ExhaustTemperature, 1.0),
            (float)solver.LoopFlux,
            useForNominal: false
        );
    }

    public override void UpdateThrottle()
    {
        // We actually want the default throttle behaviour here so we use a
        // harmony reverse patch to ensure it actually gets called.
        ModuleEngines_UpdateThrottle(this);
        base.UpdateThrottle();
    }

    static FloatCurve DefaultTempIspCurve()
    {
        var fc = new FloatCurve();
        fc.Add(0f, 1f);
        return fc;
    }

    public override void OnLoad(ConfigNode node)
    {
        if (part.partInfo is null)
        {
            var pnodes = node.GetNodes("PROPELLANT");

            thermalPropellants = new(pnodes.Length);
            foreach (var pnode in pnodes)
                thermalPropellants.Add(new(pnode));
        }
        else
        {
            thermalPropellants = LoadThermalPropellantsFromPrefab();
        }

        base.OnLoad(node);
    }

    List<ThermalPropellant> LoadThermalPropellantsFromPrefab()
    {
        if (part.partInfo is null)
            return [];

        var index = part.Modules.IndexOf(this);
        if (index < 0 || index >= part.Modules.Count)
            return [];

        if (part.partInfo.partPrefab.Modules[index] is not ModuleThermalEngine prefab)
            return [];

        return prefab.thermalPropellants;
    }

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(ModuleThermalEngine), nameof(ModuleEngines_UpdateThrottle))]
    static void ModuleEngines_UpdateThrottle(ModuleEngines engines)
    {
#pragma warning disable CS8321 // Local function is declared but never used
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> _)
        {
            return
            [
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(
                    OpCodes.Call,
                    SymbolExtensions.GetMethodInfo<ModuleEngines>(eng => eng.UpdateThrottle())
                ),
                new CodeInstruction(OpCodes.Ret),
            ];
        }
#pragma warning restore CS8321 // Local function is declared but never used

        throw new NotImplementedException();
    }
}
