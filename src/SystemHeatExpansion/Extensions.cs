using System;
using System.Collections.Generic;
using SystemHeat;
using UnityEngine;

namespace SystemHeatExpansion;

internal static class Extensions
{
    internal static void Deconstruct<K, V>(this KeyValuePair<K, V> pair, out K key, out V value)
    {
        key = pair.Key;
        value = pair.Value;
    }

    /// <summary>
    /// Get the total amount of flux consumed by this module. Negative values
    /// mean that we actually produced flux.
    /// </summary>
    /// <param name="module"></param>
    /// <param name="id"></param>
    /// <returns></returns>
    internal static float GetConsumedModuleFlux(this ModuleSystemHeat module, string id)
    {
        if (!module.fluxes.TryGetValue(id, out var ourFlux))
            ourFlux = 0f;

        if (ourFlux >= 0f)
            return -ourFlux;

        if (module.fluxes.Count == 1)
            return -ourFlux;

        float negativeFlux = 0f;
        float positiveFlux = 0f;

        foreach (var flux in module.fluxes.Values)
        {
            if (flux < 0f)
                negativeFlux += -flux;
            else
                positiveFlux += flux;
        }

        float consumedSystemFlux = module.consumedSystemFlux;
        if (consumedSystemFlux < 0f)
            consumedSystemFlux = -consumedSystemFlux;

        // SystemHeat doesn't handle this case correctly, and leaves consumedSystemFlux
        // as what it happened to be before in this case.
        if (module.totalSystemFlux >= 0f)
            consumedSystemFlux = 0f;

        var netNegativeFlux = consumedSystemFlux + positiveFlux;
        var frac = -ourFlux / negativeFlux;
        if (float.IsInfinity(frac) || float.IsNaN(frac))
            frac = 0f;

        return netNegativeFlux * frac;
    }
}
