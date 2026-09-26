using System;
using System.Threading.Tasks;
using UnityEngine;

public class Diagram : MonoBehaviour
{
    [SerializeField] private DiagramData data;

    [SerializeField] Animator animator;

    public async Task Despawn()
    {
        await PlayActionAnimation("DESPAWN");

        Destroy(gameObject);
    }

    public async Task Spawn()
    {
        await PlayActionAnimation("RESET");
    }

    /*public async Task CompleteActions(List<string> actionTags)
    {
        foreach (string actionTag in actionTags) 
        {
            if (actionTag == "RESET" || data.actionTags.Contains(actionTag)) 
            {
                await PlayActionAnimation(actionTag);
            }
        }

        Debug.Log("All actions complete for diagram: " + data.name);
    }*/

    public async Task CompleteAction(string actionTag)
    {
        if (actionTag == "RESET" || data.actionTags.Contains(actionTag))
        {
            await PlayActionAnimation(actionTag);
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
