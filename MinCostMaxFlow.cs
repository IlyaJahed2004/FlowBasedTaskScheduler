using DataModel;

namespace MinCostMaxFlow
{
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

        // Basic successive-shortest-path (Bellman-Ford to handle negative costs)
        public (int maxFlow, int minCost) GetMinCostMaxFlow()
        {
            int flow = 0,
                cost = 0;
            int n = graph.VertexCount;

            while (true)
            {
                int[] dist = new int[n];
                Edge[] parent = new Edge[n];
                for (int i = 0; i < n; i++)
                    dist[i] = int.MaxValue;
                dist[source] = 0;

                // Bellman-Ford: V-1 relaxations
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
                            int v = e.To;
                            long nd = (long)dist[u] + e.Cost;
                            if (nd < dist[v])
                            {
                                dist[v] = (int)nd;
                                parent[v] = e;
                                improved = true;
                            }
                        }
                    }
                    if (!improved)
                        break;
                }

                if (dist[sink] == int.MaxValue || parent[sink] == null)
                    break; // no augmenting path

                int aug = int.MaxValue;
                for (int v = sink; v != source; v = parent[v].From)
                    aug = Math.Min(aug, parent[v].RemainingCapacity);

                for (int v = sink; v != source; v = parent[v].From)
                {
                    var e = parent[v];
                    e.AddFlow(aug);
                    cost += aug * e.Cost;
                }

                flow += aug;
            }

            return (flow, cost);
        }
    }
}
