using System;
using System.Collections.Generic;

namespace Kmart;

public class RollbackContext
{
    public List<Action> RollbackActions = new();
    
    public RollbackContext()
    {
        
    }

    public void AddRollbackAction(Action action) => RollbackActions.Add(action);

    public void ExecuteRollback()
    {
        RollbackActions.Reverse();

        foreach (var action in RollbackActions)
        {
            action();
        }

        RollbackActions.Clear();
    }

    public void AddRollbackActions(RollbackContext context)
    {
        context.RollbackActions.ForEach(AddRollbackAction);
        context.RollbackActions.Clear();
    }
}