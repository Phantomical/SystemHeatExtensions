using HarmonyLib;
using UnityEngine;

namespace ActiveHeatshields;

[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal class HarmonyPatcher : MonoBehaviour
{
    void Awake()
    {
        var harmony = new Harmony("ActiveHeatshields");
        harmony.PatchAll(typeof(HarmonyPatcher).Assembly);
    }
}
