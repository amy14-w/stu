using UnityEngine;

public class PipExpressionState : StateMachineBehaviour
{
    public enum Expression
    {
        Base,
        Think,
        Cheer
    }

    [SerializeField] private Expression expression;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        PipExpressions pipExpressions = animator.GetComponent<PipExpressions>();
        if (pipExpressions != null)
        {
            switch (expression)
            {
                case Expression.Base:
                    pipExpressions.ShowBaseFace();
                    break;
                case Expression.Think:
                    pipExpressions.ShowThinkFace();
                    break;
                case Expression.Cheer:
                    pipExpressions.ShowCheerFace();
                    break;
            }
        }
    }
}
