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

        engineSolver = new ThermalEngineSolver(this);
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
            solver.ExhaustTemperature = solver.ComputeExhaustTemperature();
            solver.LoopFlux = solver.ComputeLoopFlux();
            solver.running = solver.LoopFlux != 0.0 && solver.ExhaustTemperature != 0.0;
        }

        HeatModule.AddFlux(
            moduleID,
            (float)Math.Max(solver.ExhaustTemperature, 1.0),
            (float)solver.LoopFlux,
            useForNominal: false
        );
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
}
