using HarmonyLib;
using UnityEngine;

namespace SystemHeatExtensions;

[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal class HarmonyPatcher : MonoBehaviour
{
    void Awake()
    {
        var harmony = new Harmony("SystemHeatExtensions");
        harmony.PatchAll(typeof(HarmonyPatcher).Assembly);
    }
}
