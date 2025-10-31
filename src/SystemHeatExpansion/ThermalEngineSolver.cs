using System;
using System.Collections.Generic;
using KSP.Localization;
using SystemHeat;
using SystemHeatExpansion.Utils;

namespace SystemHeatExpansion;

public class ThermalEngineSolver : StockalikeEngineSolver
{
    public ModuleSystemHeat HeatModule;

    // Isp Curves
    public FloatCurve TemperatureIspCurve;

    // Loop temperature parameters
    public double HeatTransferEfficiency;
    public double MinOperatingTemperature;
    public double MaxOperatingTemperature;

    // Mixture parameters
    public double MixtureSpecificHeatCapacity;
    public double MixtureInitialTemperature;
    public double MixtureVaporizationCost;

    // State
    public double ExhaustTemperature;
    public double LoopFlux;

    public ThermalEngineSolver(ModuleThermalEngine engine)
        : base(engine)
    {
        HeatModule = engine.HeatModule;
        HeatTransferEfficiency = engine.heatTransferEfficiency;
        MinOperatingTemperature = engine.minOperatingTemperature;
        MaxOperatingTemperature = engine.maxOperatingTemperature;

        SetPropellantInfo(engine.propellants, engine.thermalPropellants);
    }

    public override void CalculatePerformance(
        double airRatio,
        double commandedThrottle,
        double flowMult,
        double ispMult
    )
    {
        ExhaustTemperature = ComputeExhaustTemperature();

        base.CalculatePerformance(airRatio, commandedThrottle, flowMult, ispMult);

        LoopFlux = ComputeLoopFlux();
    }

    public virtual double ComputeExhaustTemperature()
    {
        return UtilMath.LerpUnclamped(
            MixtureInitialTemperature,
            HeatModule.LoopTemperature,
            HeatTransferEfficiency
        );
    }

    public virtual double ComputeLoopFlux()
    {
        if (!running)
            return 0.0;

        var diff = MixtureInitialTemperature - ExhaustTemperature;
        return (diff * MixtureSpecificHeatCapacity + MixtureVaporizationCost) * fuelFlow;
    }

    public override bool CanOperate()
    {
        if (!base.CanOperate())
            return false;

        var loopTemp = HeatModule.LoopTemperature;
        if (loopTemp < MinOperatingTemperature)
        {
            statusString = Localizer.GetStringByTag("#LOC_SHX_Engine_TempTooLow");
            return false;
        }

        if (loopTemp > MaxOperatingTemperature)
        {
            statusString = Localizer.GetStringByTag("#LOC_SHX_Engine_TempTooHigh");
            return false;
        }

        return true;
    }

    public override double ComputeIsp(double ispMult)
    {
        var isp = base.ComputeIsp(ispMult);
        isp *= TemperatureIspCurve?.Evaluate((float)ComputeExhaustTemperature()) ?? 1.0;
        return isp;
    }

    void SetPropellantInfo(List<Propellant> propellants, List<ThermalPropellant> thermalPropellants)
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

    public override double GetEngineTemp()
    {
        return HeatModule.LoopTemperature;
    }
}
