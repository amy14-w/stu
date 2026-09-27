using System;
using System.Threading.Tasks;
using UnityEngine;

public class Diagram : MonoBehaviour
{
    [SerializeField] private DiagramData data;

    [SerializeField] Animator animator;

    public void Despawn()
    {
        DespawnAnimation();
    }

    private async Task DespawnAnimation()
    {
        await PlayActionAnimation("DESPAWN");

        Destroy(gameObject);
    }

    public void Spawn()
    {
        animator.SetTrigger("RESET");
    }

    public void CompleteAction(string actionTag)
    {
        Debug.Log(actionTag);

        if (data.actionTags.Contains(actionTag))
        {
            Debug.Log("TRIGGER WORKED");
            animator.SetTrigger(actionTag);
        }
    }

    private async Task PlayActionAnimation(string trigger)
    {
        Debug.Log("Playing action animation for " + trigger);

        animator.SetTrigger(trigger);

        // 2. Wait 1 frame so the Animator transitions into the new state
        await Task.Yield();

        // 3. Get the length (in seconds) of the currently playing animation clip
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        float duration = stateInfo.length;

        // 4. Await for the duration of the animation clip
        await Task.Delay(TimeSpan.FromSeconds(duration));
    }
}
