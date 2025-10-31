using HarmonyLib;
using UnityEngine;

namespace SystemHeatExpansion;

[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal class HarmonyPatcher : MonoBehaviour
{
    void Awake()
    {
        var harmony = new Harmony("SystemHeatExpansion");
        harmony.PatchAll(typeof(HarmonyPatcher).Assembly);
    }
}
