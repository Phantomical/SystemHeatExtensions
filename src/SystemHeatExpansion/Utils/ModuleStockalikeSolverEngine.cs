using System;
using System.Linq.Expressions;
using System.Reflection;
using Experience.Effects;
using SolverEngines;

namespace SystemHeatExpansion.Utils;

public abstract class ModuleStockalikeSolverEngines : ModuleEnginesSolver
{
    private StockalikeEngineSolver Solver => (StockalikeEngineSolver)engineSolver;

    public override void CreateEngine()
    {
        engineSolver = new StockalikeEngineSolver(this);
    }

    public override void OnStart(StartState state)
    {
        if (maxEngineTemp == 0.0)
            maxEngineTemp = part.maxTemp;

        base.OnStart(state);
    }

    public override void FixedUpdate()
    {
        CreateEngineIfNecessary();
        var solver = Solver;

        if (HighLogic.LoadedSceneIsFlight)
            solver.SetThermalState(part.temperature);
        else
            solver.SetThermalState(273.1d);

        base.FixedUpdate();

        if (HighLogic.LoadedSceneIsFlight && solver.HeatProduction != 0d)
            part.AddThermalFlux(solver.HeatProduction);
    }

    public override void UpdateThrottle()
    {
        BaseUpdateThrottle();
        base.UpdateThrottle();
    }

    static readonly Action<ModuleEngines> UpdateThrottleDelegate = GetUpdateThrottleDelegate();

    protected void BaseUpdateThrottle()
    {
        UpdateThrottleDelegate(this);
    }

    static Action<ModuleEngines> GetUpdateThrottleDelegate()
    {
        var method = typeof(ModuleEngines).GetMethod(
            "UpdateThrottle",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            [],
            null
        );

        var param = Expression.Parameter(typeof(ModuleEngines), "module");
        var expr = Expression.Lambda<Action<ModuleEngines>>(Expression.Call(param, method), param);

        return expr.Compile();
    }
}
