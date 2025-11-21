using System;
using Expansions.Missions.Editor;
using SystemHeat;

namespace SystemHeatExpansion.Utils;

/// <summary>
/// When we have multiple different modules interacting on the same loop we
/// want the individual consumers to scale down their consumption of heat
/// to match the production in the system.
///
/// SystemHeat doesn't do this so you can end up with a loop at 1000K with
/// 8MW of heat being produced and 14MW of heat being consumed. This is no good,
/// we want the loop heat temperature to actually make sense.
///
/// This module has a shared PD controller that can be used to scale the
/// consumption of a loop consumer in a way that should work relatively well no
/// matter how many other controllers there are doing the same thing in the
/// loop.
/// </summary>
public class FluxController()
{
    static readonly double Kp = 0.5;
    static readonly double Kd = 0.1;

    HeatLoop Loop;
    SystemHeatExpansionVessel VesselModule;

    double LastError = 0.0;
    double Control0 = 0.0;
    double Control1 = 0.0;
    double Scale = 0.0;

    /// <summary>
    /// An estimate of the maximum amount of change that this controller could
    /// make to the error.
    /// </summary>
    public double ScaleEstimate = 0.0;

    /// <summary>
    /// Is the control value returned being used to control heat consumption
    /// or heat production? This affects how the error value is computed.
    /// </summary>
    public bool IsConsumer = true;

    public bool IsSetup => Loop is not null;
    public double Current => IsSetup ? Control1 : 0.0;

    public void Setup(HeatLoop loop, Vessel vessel)
    {
        if (loop is null)
        {
            Loop = null;
            return;
        }

        if (!IsSetup)
        {
            Control0 = Control1 = 0.0;

            // We have no idea how much of an effect we have on the net flux, start
            // with an estimate that all negative flux on the system is due to us
            // (or other consumers running the same control algorithm).
            if (IsConsumer)
                Scale = Math.Abs(loop.NegativeFlux);
            else
                Scale = Math.Abs(loop.PositiveFlux);
        }

        LastError = GetError();
        Loop = loop;
        VesselModule = vessel?.FindVesselModuleImplementing<SystemHeatExpansionVessel>();
    }

    public void Clear()
    {
        Loop = null;
    }

    public double Update(HeatLoop loop, Vessel vessel, double dt)
    {
        if (!ReferenceEquals(Loop, loop))
            Setup(loop, vessel);

        if (Loop is null)
            return 0.0;

        double error = GetError();
        double scale = GetScaleEstimate(error);
        if (scale == 0.0)
            scale = 1.0;

        var p = Kp * error / scale;
        var d = Kd * (error - LastError) / scale / dt;
        var c = UtilMath.Clamp01(Control1 + p + d);

        Control0 = Control1;
        Control1 = c;
        LastError = error;
        Scale = scale;

        return c;
    }

    static double ErrMult = 1.0;
    static double ErrScale = 100.0;

    double GetError()
    {
        if (Loop is null)
            return 0.0;

        double netUsefulFlux = VesselModule?.GetNetUsefulFlux(Loop.ID) ?? Loop.NetFlux;
        double tdiff = (Loop.Temperature - Loop.NominalTemperature) * ErrScale;
        double error = netUsefulFlux + tdiff;
        error *= ErrMult;

        if (IsConsumer)
            return error;
        else
            return -error;
    }

    double GetScaleEstimate(double error)
    {
        double df = error - LastError;
        double dc = Control1 - Control0;

        if (df == 0.0 || dc == 0.0)
            return Scale;
        if (ScaleEstimate < 0.0)
            ScaleEstimate = -ScaleEstimate;

        double max = IsConsumer
            ? Math.Abs(Loop.NegativeFlux) + (1.0 - Control1) * ScaleEstimate
            : Math.Abs(Loop.PositiveFlux) + (1.0 - Control1) * ScaleEstimate;

        double scale = Math.Min(Math.Abs(df / dc), max);
        if (scale == 0.0)
            scale = ScaleEstimate;
        return scale;
    }
}
