using UnityEngine;

public enum NodeState { Running, Success, Failure }

// Nodul de baza
public abstract class BTNode
{
    public abstract NodeState Evaluate();
}

// Selector - incearca nodurile pana unul reuseste
public class BTSelector : BTNode
{
    private BTNode[] children;
    public BTSelector(params BTNode[] children) { this.children = children; }

    public override NodeState Evaluate()
    {
        foreach (var child in children)
        {
            NodeState result = child.Evaluate();
            if (result != NodeState.Failure) return result;
        }
        return NodeState.Failure;
    }
}

// Sequence - executa nodurile in ordine, se opreste la primul esec
public class BTSequence : BTNode
{
    private BTNode[] children;
    public BTSequence(params BTNode[] children) { this.children = children; }

    public override NodeState Evaluate()
    {
        foreach (var child in children)
        {
            NodeState result = child.Evaluate();
            if (result != NodeState.Success) return result;
        }
        return NodeState.Success;
    }
}

// Condition - verifica o conditie
public class BTCondition : BTNode
{
    private System.Func<bool> condition;
    public BTCondition(System.Func<bool> condition) { this.condition = condition; }

    public override NodeState Evaluate()
    {
        return condition() ? NodeState.Success : NodeState.Failure;
    }
}

// Action - executa o actiune
public class BTAction : BTNode
{
    private System.Func<NodeState> action;
    public BTAction(System.Func<NodeState> action) { this.action = action; }

    public override NodeState Evaluate()
    {
        return action();
    }
}