using DataModel;

namespace MinCostMaxFlow;

public class MinCostMaxFlow
{
    private readonly Graph graph;
    private readonly int source,
        sink;

    public MinCostMaxFlow(Graph g, int s, int t)
    {
        graph = g;
        source = s;
        sink = t;
    }

    public (int maxFlow, int minCost) GetMinCostMaxFlow()
    {
        int flow = 0,
            cost = 0;
        int n = graph.VertexCount;

        while (true)
        {
            // 1) init distances/parents
            int[] dist = new int[n];
            Edge[] parent = new Edge[n];
            for (int i = 0; i < n; i++)
                dist[i] = int.MaxValue;
            dist[source] = 0;

            // 2) Bellman–Ford: do V-1 full relaxations over ALL edges
            for (int it = 0; it < n - 1; it++)
            {
                bool improved = false;

                for (int u = 0; u < n; u++)
                {
                    if (dist[u] == int.MaxValue)
                        continue;

                    foreach (var e in graph.Adj[u])
                    {
                        if (e.RemainingCapacity <= 0)
                            continue;

                        int aimedvertex = e.To;
                        int regularcost = dist[u] + e.Cost;
                        if (regularcost < dist[aimedvertex])
                        {
                            dist[aimedvertex] = regularcost;
                            parent[aimedvertex] = e;
                            improved = true;
                        }
                    }
                }

                if (!improved)
                    break;
            }

            // 3) if sink is unreachable, no more augmenting paths → stop
            if (dist[sink] == int.MaxValue || parent[sink] == null)
                break;

            // 4) find bottleneck capacity (minimum remaining capacity along the path)
            int augFlow = int.MaxValue;
            for (int v = sink; v != source; v = parent[v].From)
                augFlow = Math.Min(augFlow, parent[v].RemainingCapacity);

            // 5) update flows along the path and accumulate total cost
            for (int v = sink; v != source; v = parent[v].From)
            {
                var e = parent[v];
                e.AddFlow(augFlow);
                cost += augFlow * e.Cost; // add path cost per unit * flow units
            }

            // increase total flow
            flow += augFlow;
        }
        return (flow, cost);
    }
}
