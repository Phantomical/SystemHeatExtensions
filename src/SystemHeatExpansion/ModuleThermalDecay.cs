using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using HarmonyLib;
using KSP.Localization;
using SystemHeat;
using UnityEngine;

namespace SystemHeatExpansion;

/// <summary>
/// A module that decays resources on contained within the current part. You can
/// use it to cause thermal decay or to have the resource produce waste heat.
/// </summary>
[KSPModule("#LOC_SHX_ModuleSHXThermalDecay_ModuleName")]
public class ModuleSHXThermalDecay : PartModule
{
    /// <summary>
    /// Leakage configs for individual resources. These are defined using
    /// <c>RESOURCE</c> nodes within the module.
    /// </summary>
    public struct DecayResource() : IConfigNode
    {
        /// <summary>
        /// The resource name.
        /// </summary>
        public string name;

        /// <summary>
        /// The resource id corresponding to <see cref="name"/>.
        /// </summary>
        public int id;

        /// <summary>
        /// The module ID used when emitting flux for this specific resource.
        /// </summary>
        public string moduleID;

        /// <summary>
        /// The temperature that the flux will be emitted at.
        /// </summary>
        public double temperature = 0.0;

        /// <summary>
        /// The amount of flux that will be emitted, in kW/unit. For resources
        /// currently contained within the tank.
        /// </summary>
        public double resourceFlux = 0.0;

        /// <summary>
        /// The amount of resource that will be lost each hour and converted
        /// to heat (in %/Hr).
        /// </summary>
        public double decayRate = 0.0;

        /// <summary>
        /// How much flux is emitted based on the current decay rate.
        /// </summary>
        public double decayFlux = 0.0;

        /// <summary>
        /// Indicate that fluxes are specified in kW/t instead of kW/unit.
        /// </summary>
        public bool fluxByMass = false;

        public DecayResource(ConfigNode node)
            : this()
        {
            Load(node);
        }

        public void Load(ConfigNode node)
        {
            node.TryGetValue(nameof(name), ref name);
            node.TryGetValue(nameof(temperature), ref temperature);
            node.TryGetValue(nameof(resourceFlux), ref resourceFlux);
            node.TryGetValue(nameof(fluxByMass), ref fluxByMass);
            node.TryGetValue(nameof(decayRate), ref decayRate);
            node.TryGetValue(nameof(decayFlux), ref decayFlux);

            id = name?.GetHashCode() ?? 0;
        }

        public void Save(ConfigNode node)
        {
            node.AddValue(nameof(name), name);
            node.AddValue(nameof(temperature), temperature);
            node.AddValue(nameof(resourceFlux), resourceFlux);
            node.AddValue(nameof(fluxByMass), fluxByMass);
            node.AddValue(nameof(decayRate), decayRate);
            node.AddValue(nameof(decayFlux), decayFlux);
        }
    }

    public ModuleSystemHeat HeatModule;

    public List<DecayResource> ResourceConfigs;

    /// <summary>
    /// A unique identifier for this module on the current part.
    /// </summary>
    [KSPField]
    public string moduleID = "ModuleSHXThermalDecay";

    /// <summary>
    /// This should correspond to the related ModuleSystemHeat. If not specified,
    /// the first found module will be used.
    /// </summary>
    [KSPField]
    public string systemHeatModuleID;

    [KSPField(isPersistant = true)]
    public double LastUpdateTime;

    public override string GetInfo()
    {
        StringBuilder builder = new();
        builder.Append(Localizer.Format("#LOC_SHX_ModuleSHXThermalDecay_PartInfo"));

        foreach (var config in ResourceConfigs)
        {
            string sub;
            string resname =
                PartResourceLibrary.Instance.resourceDefinitions[config.id]?.displayName
                ?? config.name;
            if (config.fluxByMass)
            {
                sub = Localizer.Format(
                    "#LOC_SHX_ModuleSHXThermalDecay_HeatGenerationByMass",
                    resname,
                    config.resourceFlux.ToString("F2"),
                    config.temperature.ToString("F1"),
                    config.decayRate.ToString("F2"),
                    config.decayFlux.ToString("F2")
                );
            }
            else
            {
                sub = Localizer.Format(
                    "#LOC_SHX_ModuleSHXThermalDecay_HeatGenerationByUnit",
                    resname,
                    (config.resourceFlux / 1000.0).ToString("F2"),
                    config.temperature.ToString("F1"),
                    config.decayRate.ToString("F2"),
                    (config.decayFlux / 1000.0).ToString("F2")
                );
            }

            builder.Append(sub);
        }

        return builder.ToString();
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        HeatModule ??= ModuleUtils.FindHeatModule(part, systemHeatModuleID);
        ResourceConfigs ??= LoadConfigsFromPrefab();

        GameEvents.onPartResourceListChange.Add(OnPartResourceListChange);

        DoCatchup();
    }

    public void OnDestroy()
    {
        GameEvents.onPartResourceListChange.Remove(OnPartResourceListChange);
    }

    public void FixedUpdate()
    {
        if (HeatModule is null)
            return;
        if (HighLogic.LoadedSceneIsFlight && LastUpdateTime == 0d)
        {
            LastUpdateTime = Planetarium.GetUniversalTime();
            return;
        }

        var resources = part.Resources.dict;

        foreach (var config in ResourceConfigs)
        {
            if (config.temperature == 0.0)
                continue;

            if (!resources.TryGetValue(config.id, out var resource))
                continue;

            var mult = 1d;
            if (config.fluxByMass)
                mult *= PartResourceLibrary.Instance.resourceDefinitions[config.id]?.density ?? 0.0;

            var flux = resource.amount * config.resourceFlux;
            flux += DoResourceDecay(in config, resource);
            flux *= mult;

            HeatModule.AddFlux(config.moduleID, (float)config.temperature, (float)flux, true);
        }

        if (HighLogic.LoadedSceneIsFlight)
            LastUpdateTime = Planetarium.GetUniversalTime();
    }

    double DoResourceDecay(in DecayResource config, PartResource resource)
    {
        if (config.decayRate == 0d)
            return 0d;

        var decayRate = config.decayRate * (0.01 / 3600d);

        if (!HighLogic.LoadedSceneIsFlight)
            return decayRate * resource.amount * config.decayFlux;

        if (resource.amount == 0d)
            return 0d;

        var delta = Planetarium.GetUniversalTime() - LastUpdateTime;
        var amount = resource.amount * (1d - Math.Pow(1d - decayRate, delta));
        var flowState = resource.flowState;

        // This handles if the flow has been disabled in the UI.
        resource.flowState = true;
        amount = part.RequestResource(config.id, amount, ResourceFlowMode.NO_FLOW);
        resource.flowState = flowState;

        return amount / TimeWarp.fixedDeltaTime * config.decayFlux;
    }

    void DoCatchup()
    {
        if (!HighLogic.LoadedSceneIsFlight)
            return;
        if (LastUpdateTime == 0d)
        {
            LastUpdateTime = Planetarium.GetUniversalTime();
            return;
        }

        var resources = part.Resources.dict;

        // Avoid doing any SystemHeat stuff for catchup so we don't dump a
        // massive amount of flux into the system all at once.
        foreach (var config in ResourceConfigs)
        {
            if (config.decayRate == 0.0)
                continue;
            if (!resources.TryGetValue(config.id, out var resource))
                continue;

            DoResourceDecay(in config, resource);
        }

        LastUpdateTime = Planetarium.GetUniversalTime();
    }

    void OnPartResourceListChange(Part part)
    {
        if (this.part != part)
            return;
        if (HeatModule is null)
            return;

        var fluxes = GetFluxes(HeatModule);
        if (fluxes is null)
            return;

        var resources = this.part.Resources.dict;
        foreach (var config in ResourceConfigs)
        {
            if (resources.ContainsKey(config.id))
                continue;
            if (fluxes.ContainsKey(config.moduleID))
                continue;

            // Zero out the flux for the resource that has been removed.
            HeatModule.AddFlux(config.moduleID, (float)config.temperature, 0f, true);
        }
    }

    public override void OnLoad(ConfigNode node)
    {
        base.OnLoad(node);

        if (part.partInfo is null)
        {
            var nodes = node.GetNodes("RESOURCE");
            var configs = new List<DecayResource>(nodes.Length);
            foreach (var lnode in nodes)
            {
                var config = new DecayResource(lnode);
                if (config.name is null)
                {
                    Debug.LogError(
                        $"[SystemHeatExpansion] ModuleSHXThermalLeakage RESOURCE node was missing a `name` key"
                    );
                    continue;
                }

                config.moduleID = $"{moduleID}:{config.name}";
                configs.Add(config);
            }

            ResourceConfigs = configs;
        }
        else
        {
            ResourceConfigs = LoadConfigsFromPrefab();
        }
    }

    List<DecayResource> LoadConfigsFromPrefab()
    {
        if (part.partInfo is null)
            return [];

        var index = part.Modules.IndexOf(this);
        if (index < 0 || index >= part.Modules.Count)
            return [];

        if (part.partInfo.partPrefab.Modules[index] is not ModuleSHXThermalDecay prefab)
            return [];

        return prefab.ResourceConfigs;
    }

    static readonly Func<ModuleSystemHeat, Dictionary<string, float>> GetFluxes =
        GetFluxesAccessor();

    static Func<ModuleSystemHeat, Dictionary<string, float>> GetFluxesAccessor()
    {
        var field = AccessTools.Field(typeof(ModuleSystemHeat), "fluxes");

        var param = Expression.Parameter(typeof(ModuleSystemHeat), "module");
        var access = Expression.Field(param, field);

        return Expression
            .Lambda<Func<ModuleSystemHeat, Dictionary<string, float>>>(access, param)
            .Compile();
    }
}
