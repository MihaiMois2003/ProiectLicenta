using System;

public enum NodeState { Running, Success, Failure }

public abstract class BTNode
{
    public abstract NodeState Evaluate();
}

public class BTSelector : BTNode
{
    private BTNode[] children;
    public BTSelector(params BTNode[] children) { this.children = children; }

    public override NodeState Evaluate()
    {
        foreach (var child in children)
        {
            var result = child.Evaluate();
            if (result != NodeState.Failure) return result;
        }
        return NodeState.Failure;
    }
}

public class BTSequence : BTNode
{
    private BTNode[] children;
    public BTSequence(params BTNode[] children) { this.children = children; }

    public override NodeState Evaluate()
    {
        foreach (var child in children)
        {
            var result = child.Evaluate();
            if (result != NodeState.Success) return result;
        }
        return NodeState.Success;
    }
}

public class BTCondition : BTNode
{
    private Func<bool> condition;
    public BTCondition(Func<bool> condition) { this.condition = condition; }

    public override NodeState Evaluate()
    {
        return condition() ? NodeState.Success : NodeState.Failure;
    }
}

public class BTAction : BTNode
{
    private Func<NodeState> action;
    public BTAction(Func<NodeState> action) { this.action = action; }

    public override NodeState Evaluate()
    {
        return action();
    }
}