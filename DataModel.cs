namespace DataModel
{
    public record Task(string Id, int CpuRequired, int RamRequired, int deadline);

    public record Node(string Id, int CpuCapacity, int RamCapacity, int Slots);

    public class Edge
    {
        public int From { get; init; }
        public int To { get; init; }
        public int Capacity { get; init; }
        public int Cost { get; init; } // cost per unit flow
        public int Flow { get; private set; }
        public Edge Reverse { get; set; } = null!;

        public int RemainingCapacity => Capacity - Flow;

        public void AddFlow(int f)
        {
            Flow += f;
            Reverse.Flow -= f;
        }

        // Utility to reset flow (used by Graph.ResetFlows)
        public void ResetFlow()
        {
            Flow = 0;
        }
    }

    public class Graph
    {
        public int VertexCount { get; }
        public List<Edge>[] Adj { get; }

        public Graph(int vertexCount)
        {
            VertexCount = vertexCount;
            Adj = new List<Edge>[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                Adj[i] = new List<Edge>();
        }

        public void AddEdge(int from, int to, int capacity, int cost)
        {
            var e1 = new Edge
            {
                From = from,
                To = to,
                Capacity = capacity,
                Cost = cost,
            };
            var e2 = new Edge
            {
                From = to,
                To = from,
                Capacity = 0,
                Cost = -cost,
            };
            e1.Reverse = e2;
            e2.Reverse = e1;
            Adj[from].Add(e1);
            Adj[to].Add(e2);
        }

        // NEW: reset flows on all edges to allow re-running MCMF safely
        public void ResetFlows()
        {
            for (int u = 0; u < VertexCount; u++)
            {
                foreach (var e in Adj[u])
                {
                    e.ResetFlow();
                }
            }
        }
    }
}
