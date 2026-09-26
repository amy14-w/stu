using System.Collections.Generic;
using System.Text.RegularExpressions;
using System;
using System.Threading.Tasks;
using UnityEngine;

public class ConversationHandlerTest : MonoBehaviour
{
    public void Test(string key)
    {
        Prompt(key);
    }

    public async Task Prompt(string problemKey)
    {
        var problem = ProblemsManager.Instance.RetrieveProblem(problemKey);
        if (problem != null) 
        {
            ProblemsManager.Instance.TryProblemChange(problem);
            await ParseOutput("[SHOW DIAGRAM]Sure here is an example");

            await ParseOutput("Lets grow the sphere [A:GROW_SPHERE]");
        }
    }

    private async Task ParseOutput(string output)
    {
        // Matches anything inside square brackets e.g. [SHOW DIAGRAM] or [A:ROTATE]
        string pattern = @"\[(.*?)\]";
        MatchCollection matches = Regex.Matches(output, pattern);

        foreach (Match match in matches)
        {
            string tag = match.Groups[1].Value.Trim();

            // 1. Handle special tag: SHOW DIAGRAM
            if (tag.Equals("SHOW DIAGRAM", StringComparison.OrdinalIgnoreCase))
            {
                var problem = ProblemsManager.Instance.GetCurrentProblem();
                Debug.Log(problem.diagram);

                await DiagramManager.Instance.TrySpawnDiagram(problem.diagram);
            }
            // 2. Handle special tag: HIDE DIAGRAM
            else if (tag.Equals("HIDE DIAGRAM", StringComparison.OrdinalIgnoreCase))
            {
                await DiagramManager.Instance.DespawnDiagram();
            }
            // 3. Handle Action tags starting with "A:"
            else if (tag.StartsWith("A:", StringComparison.OrdinalIgnoreCase))
            {
                if (DiagramManager.Instance.IsDiagramActive())
                {
                    // Extract action name after "A:"
                    string actionName = tag.Substring(2).Trim().ToUpper();

                    await DiagramManager.Instance.CompleteDiagramAction(actionName);
                }
            }
        }

        Debug.Log("Finished reacting to LLM output");
    }
}
