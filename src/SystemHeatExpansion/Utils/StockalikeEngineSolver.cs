using System;
using System.Collections.Generic;
using KSP.Localization;
using SolverEngines;

namespace SystemHeatExpansion.Utils;

/// <summary>
/// This is an engine solver that is designed to work almost exactly like the
/// stock engine.
/// </summary>
public class StockalikeEngineSolver : EngineSolver
{
    // Propellants
    public List<Propellant> Propellants;

    // ISP parameters
    public FloatCurve AtmosphereCurve;
    public FloatCurve AtmCurveIsp;
    public FloatCurve VelCurveIsp;
    public FloatCurve ThrottleIspCurve;
    public FloatCurve ThrottleIspCurveAtmStrength;

    // Fuel Flow parameters
    public FloatCurve AtmCurve;
    public FloatCurve VelCurve;
    public FloatCurve ThrustCurve;
    public double MaxFuelFlow;
    public bool AtmChangeFlow;
    public double FlowMultCap;
    public double FlowMultCapSharpness;

    // Thrust Parameters
    public double G;

    // Flameout
    public double FlameoutBar;
    public bool DisableUnderwater;

    // Heating
    public double MachLimit;
    public double MachHeatMult;
    public double EngineHeatProduction;
    public double Mass;
    public bool NormalizeHeatForFlow;

    // State
    public double FlowMultiplier;
    public double HeatProduction;
    public double Temperature;

    public StockalikeEngineSolver(ModuleEngines engine)
    {
        Propellants = engine.propellants;

        AtmosphereCurve = engine.atmosphereCurve;
        AtmCurveIsp = engine.useAtmCurveIsp ? engine.atmCurveIsp : null;
        VelCurveIsp = engine.useVelCurveIsp ? engine.velCurveIsp : null;
        ThrottleIspCurve = engine.useThrottleIspCurve ? engine.throttleIspCurve : null;
        ThrottleIspCurveAtmStrength = engine.useThrottleIspCurve
            ? engine.throttleIspCurveAtmStrength
            : null;

        AtmCurve = engine.useAtmCurve ? engine.atmCurve : null;
        VelCurve = engine.useVelCurve ? engine.velCurve : null;
        ThrustCurve = engine.useThrustCurve ? engine.thrustCurve : null;
        MaxFuelFlow = engine.maxFuelFlow;
        AtmChangeFlow = engine.atmChangeFlow;
        FlowMultCap = engine.flowMultCap;
        FlowMultCapSharpness = engine.flowMultCapSharpness;

        G = engine.g;

        FlameoutBar = engine.flameoutBar;
        DisableUnderwater = engine.disableUnderwater;

        MachLimit = engine.machLimit;
        MachHeatMult = engine.machHeatMult;
        HeatProduction = engine.heatProduction;
        Mass = engine.part.mass;
        NormalizeHeatForFlow = engine.normalizeHeatForFlow;
    }

    public virtual void SetThermalState(double temp)
    {
        Temperature = temp;
    }

    public override double GetEngineTemp() => Temperature;

    public override void CalculatePerformance(
        double airRatio,
        double commandedThrottle,
        double flowMult,
        double ispMult
    )
    {
        base.CalculatePerformance(airRatio, commandedThrottle, flowMult, ispMult);

        Isp = ComputeIsp(ispMult);
        SFC = 3600d / Isp;
        FlowMultiplier = ComputeFlowModifier(flowMult);
        fuelFlow = ComputeFuelFlow(throttle);
        thrust = ComputeThrust(fuelFlow);
        running = CanOperate();

        if (running)
        {
            HeatProduction = ComputeHeatProduction();
        }
        else
        {
            thrust = 0.0;
            fuelFlow = 0.0;
            HeatProduction = 0.0;
        }
    }

    /// <summary>
    /// Compute the current isp of the engine.
    /// </summary>
    /// <param name="ispMult">An isp multiplier provided by the solver engine module.</param>
    /// <returns></returns>
    public virtual double ComputeIsp(double ispMult)
    {
        double atm = p0 * 0.001 * PhysicsGlobals.KpaToAtmospheres;
        double isp = ispMult;
        isp *= AtmosphereCurve?.Evaluate((float)atm) ?? 1.0;
        isp *= ComputeThrottlingMult(atm, throttle);
        isp *= AtmCurveIsp?.Evaluate((float)(rho * (1.0 / 1.225))) ?? 1.0;
        isp *= VelCurveIsp?.Evaluate((float)mach) ?? 1.0;
        return isp;
    }

    /// <summary>
    /// Compute the requested fuel flow.
    /// </summary>
    /// <returns></returns>
    public virtual double ComputeFuelFlow(double throttle)
    {
        return MaxFuelFlow * throttle * FlowMultiplier;
    }

    /// <summary>
    /// Compute the current thrust.
    /// </summary>
    /// <returns></returns>
    public virtual double ComputeThrust(double fuelFlow)
    {
        return ffFraction * fuelFlow * Isp * G;
    }

    public virtual double ComputeHeatProduction()
    {
        double mult = 1.0;
        if (mach > MachLimit)
        {
            double atm = p0 * 0.001 * PhysicsGlobals.KpaToAtmospheres;
            mult += (mach - MachLimit) * atm * MachHeatMult;
        }

        double kilowatts = mult * EngineHeatProduction;
        if (NormalizeHeatForFlow)
            kilowatts /= FlowMultiplier;

        double maxThrust = ComputeThrust(ComputeFuelFlow(1.0));
        kilowatts *= thrust / maxThrust;
        kilowatts *= PhysicsGlobals.InternalHeatProductionFactor;

        return kilowatts;
    }

    /// <summary>
    /// Determines whether hte engine can operate and also sets the desired status string.
    ///
    /// This gets called after all the engine parameters have been computed.
    /// </summary>
    /// <returns></returns>
    public virtual bool CanOperate()
    {
        statusString = ModuleEngines.cacheAutoLOC_219034; // Nominal

        if (throttle <= 0.0)
        {
            statusString = ModuleEngines.cacheAutoLOC_220477; // Off
            return false;
        }

        if (FlowMultiplier < FlameoutBar)
        {
            statusString = ModuleEngines.cacheAutoLOC_220370; // Flameout!
            return false;
        }

        if (DisableUnderwater && underwater)
        {
            statusString = ModuleEngines.cacheAutoLOC_220377; // Underwater!
            return false;
        }

        if (CheatOptions.InfinitePropellant)
            return true;

        if (running && ffFraction == 0.0)
        {
            statusString = FuelDeprived;
            return false;
        }

        return true;
    }

    public virtual double ComputeFlowModifier(double flowMult)
    {
        double mult = 1.0;
        if (AtmChangeFlow)
        {
            mult = rho * (1.0 / 1.225);
            mult = AtmCurve?.Evaluate((float)mult) ?? mult;
        }

        mult *= flowMult;
        mult *= VelCurve?.Evaluate((float)mach) ?? 1.0;

        if (ThrustCurve is not null)
        {
            double ratio = 1.0;

            foreach (var propellant in Propellants)
            {
                if (propellant.ignoreForThrustCurve)
                    continue;

                ratio = Math.Min(
                    ratio,
                    propellant.totalResourceAvailable / propellant.totalResourceCapacity
                );
            }

            mult *= ThrustCurve.Evaluate((float)ratio);
        }

        if (mult > FlowMultCap)
        {
            double diff = mult - FlowMultCap;
            mult = FlowMultCap + diff / (FlowMultCapSharpness + diff / FlowMultCap);
        }

        return Math.Max(mult, 1e-5);
    }

    public virtual double ComputeThrottlingMult(double atm, double throttle)
    {
        return UtilMath.Lerp(
            1.0,
            ThrottleIspCurve?.Evaluate((float)throttle) ?? throttle,
            ThrottleIspCurveAtmStrength?.Evaluate((float)atm) ?? 0.0
        );
    }

    private static readonly string FuelDeprived = Localizer.GetStringByTag(
        "#LOC_SHX_Engine_FuelDeprived"
    );
}
