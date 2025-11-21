using System;
using SystemHeat;

namespace SystemHeatExpansion;

public class SystemHeatExpansionVessel : VesselModule
{
    SystemHeatVessel systemHeat;

    readonly float[] usefulFluxConsumption = new float[10];
    readonly float[] netUsefulFlux = new float[10];

    public override void OnStart()
    {
        systemHeat = Vessel.FindVesselModuleImplementing<SystemHeatVessel>();
    }

    public float GetNetUsefulFlux(int loopId)
    {
        if (loopId < 0 || loopId >= 10)
            return 0f;
        return usefulFluxConsumption[loopId];
    }

    public float GetUsefulFluxConsumption(int loopId)
    {
        if (loopId < 0 || loopId >= 10)
            return 0f;
        return netUsefulFlux[loopId];
    }

    internal void OnVesselSystemHeatUpdated()
    {
        Array.Clear(usefulFluxConsumption, 0, 10);
        Array.Clear(netUsefulFlux, 0, 10);

        if (systemHeat?.Simulator?.HeatLoops is null)
            return;

        foreach (var loop in systemHeat.Simulator.HeatLoops)
        {
            float usefulFluxConsumption = 0f;

            foreach (var module in loop.LoopModules)
            {
                if (module.totalSystemFlux >= 0f)
                    continue;
                if (module.priority < 10)
                    continue;

                usefulFluxConsumption += module.totalSystemFlux;
            }

            this.usefulFluxConsumption[loop.ID] = usefulFluxConsumption;
            this.netUsefulFlux[loop.ID] = loop.PositiveFlux + usefulFluxConsumption;
        }
    }
}
