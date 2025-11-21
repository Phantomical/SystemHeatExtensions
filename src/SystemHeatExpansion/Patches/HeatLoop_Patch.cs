using System.Linq;
using HarmonyLib;
using SystemHeat;
using UnityEngine;

namespace SystemHeatExpansion.Patches;

[HarmonyPatch(typeof(SystemHeatVessel), "FixedUpdate")]
internal static class SystemHeatVessel_FixedUpdate_Patch
{
    static void Postfix(SystemHeatVessel __instance)
    {
        var shxmodule = __instance.Vessel.FindVesselModuleImplementing<SystemHeatExpansionVessel>();
        if (shxmodule is null)
            return;

        shxmodule.OnVesselSystemHeatUpdated();
    }
}

[HarmonyPatch(typeof(HeatLoop), "SimulateIteration")]
internal static class HeatLoop_SimulateIteration_Patch
{
    static void Prefix(HeatLoop __instance, out float __state)
    {
        __state = __instance.Temperature;
    }

    static void Postfix(HeatLoop __instance, float simTimeStep, float __state)
    {
        var loop = __instance;

        // How much temperature have we lost in this step?
        var tempLoss = Mathf.Max(__state - loop.Temperature, 0f);

        // ... and how much energy did that take?
        var energyLoss =
            tempLoss
            * 0.001f
            * loop.Volume
            * loop.CoolantType.Density
            * loop.CoolantType.HeatCapacity;

        // Now if we spread that out over the time step how how much extra flux do we get?
        var lossFlux = energyLoss / simTimeStep;

        AllocateFlux(loop, loop.PositiveFlux + lossFlux);
    }

    static void AllocateFlux(HeatLoop loop, float totalFlux)
    {
        ModuleSystemHeat[] consumers =
        [
            .. loop
                .LoopModules.Where(m => m.totalSystemFlux < 0f)
                .OrderByDescending(m => m.priority),
        ];

        foreach (var module in loop.LoopModules)
            module.consumedSystemFlux = 0f;

        foreach (var consumer in consumers)
        {
            var systemFlux = -consumer.totalSystemFlux;
            if (totalFlux < systemFlux)
                systemFlux = totalFlux;

            totalFlux -= systemFlux;
            consumer.consumedSystemFlux = -systemFlux;
        }
    }
}
