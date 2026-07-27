using System.Collections.Generic;
using UnityEngine;

public class WaypointRouteManager : MonoBehaviour
{
    public List<Waypoint> FindRoute(Waypoint start, Waypoint goal)
    {
        if (start == null || goal == null)
        {
            return new List<Waypoint>();
        }

        if (start == goal)
        {
            return new List<Waypoint> { start };
        }

        Queue<Waypoint> queue = new Queue<Waypoint>();
        Dictionary<Waypoint, Waypoint> previous = new Dictionary<Waypoint, Waypoint>();
        HashSet<Waypoint> visited = new HashSet<Waypoint>();

        queue.Enqueue(start);
        visited.Add(start);
        previous[start] = null;

        while (queue.Count > 0)
        {
            Waypoint current = queue.Dequeue();

            if (current == goal)
            {
                return BuildRoute(previous, goal);
            }

            foreach (Waypoint next in current.nextWaypoints)
            {
                if (next == null)
                {
                    continue;
                }

                if (visited.Contains(next))
                {
                    continue;
                }

                visited.Add(next);
                previous[next] = current;
                queue.Enqueue(next);
            }
        }

        Debug.LogWarning($"ルートが見つかりません: {start.name} → {goal.name}");
        return new List<Waypoint>();
    }

    private List<Waypoint> BuildRoute(Dictionary<Waypoint, Waypoint> previous, Waypoint goal)
    {
        List<Waypoint> route = new List<Waypoint>();

        Waypoint current = goal;

        while (current != null)
        {
            route.Add(current);
            current = previous[current];
        }

        route.Reverse();
        return route;
    }
}