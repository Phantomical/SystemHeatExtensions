using System;
using SystemHeat;

namespace ActiveHeatshields;

/// <summary>
/// This module exchanges heat between a SystemHeat loop and optionally, either
/// the internal or skin temperature of a part.
/// </summary>
public class ModuleHeatExchanger : PartModule, IAnalyticTemperatureModifier
{
    public enum HeatSource
    {
        SKIN,
        INTERNAL,
    }

    /// <summary>
    /// A unique identifier.
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
    /// If true, then this heat collector will interface directly with the
    /// temperature of the part, instead of the skin temperature.
    /// </summary>
    [KSPField]
    public HeatSource heatSource = HeatSource.SKIN;

    /// <summary>
    /// The rate at which heat can be transferred between the part's skin and
    /// the SystemHeat loop.
    ///
    /// Normally this would be defined as x W/(m*K), but this module assumes
    /// that you have premultiplied in the thickness. In addition, the game
    /// uses kilowatts for flux. So this number should be in kW/K/m^2.
    /// </summary>
    [KSPField(isPersistant = true)]
    public double thermalConductivity = 0.173 / 0.02; // 2mm of tungsten

    /// <summary>
    /// A curve that modifies the thermal conductivity based on the temperature
    /// of the part itself.
    /// </summary>
    [KSPField]
    public FloatCurve thermalConductivityCurve = DefaultThermalConductivityCurve();

    /// <summary>
    /// The surface area of the this part that can actually collect heat. This
    /// is used to determine the overall transfer rate.
    /// </summary>
    [KSPField(isPersistant = true)]
    public double surfaceArea = 1.0;

    /// <summary>
    /// How much flux was last transferred to the SystemHeat loop.
    /// </summary>
    [KSPField(isPersistant = true)]
    public double flux = 0.0;

    [KSPField(isPersistant = true, guiActiveEditor = true, guiName = "Simulation Temperature")]
    [UI_FloatRange(minValue = 0, maxValue = 5000f)]
    public float simulationTemp = 1000f;

    public ModuleSystemHeat heatModule;

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        heatModule = ModuleUtils.FindHeatModule(part, systemHeatModuleID);
    }

    public void FixedUpdate()
    {
        if (heatModule == null)
            return;

        double temp;
        if (HighLogic.LoadedSceneIsEditor)
            temp = simulationTemp;
        else if (heatSource == HeatSource.INTERNAL)
            temp = part.temperature;
        else
            temp = part.skinTemperature;

        var flux =
            (temp - heatModule.LoopTemperature)
            * thermalConductivity
            * (thermalConductivityCurve?.Evaluate((float)temp) ?? 1f)
            * surfaceArea;

        heatModule.AddFlux(moduleID, (float)temp, (float)flux, false);
        if (!HighLogic.LoadedSceneIsEditor)
        {
            if (heatSource == HeatSource.INTERNAL)
                part.AddThermalFlux(-flux);
            else
                part.AddExposedThermalFlux(-flux);
        }

        this.flux = flux;
    }

    public void SetAnalyticTemperature(
        FlightIntegrator fi,
        double analyticTemp,
        double toBeInternal,
        double toBeSkin
    )
    {
        part.skinTemperature = Math.Min(heatModule.LoopTemperature, part.skinMaxTemp);
        part.temperature = Math.Min(heatModule.LoopTemperature, part.maxTemp);
    }

    public double GetSkinTemperature(out bool lerp)
    {
        lerp = false;
        return part.skinTemperature;
    }

    public double GetInternalTemperature(out bool lerp)
    {
        lerp = false;
        return part.temperature;
    }

    static FloatCurve DefaultThermalConductivityCurve()
    {
        FloatCurve curve = new();
        curve.Add(0f, 1f);
        return curve;
    }
}
