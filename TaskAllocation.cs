using System.Text.Json;
using DataModel;

namespace Allocation
{
    public class TaskAllocator
    {
        private readonly List<DataModel.Task> tasks;
        private readonly List<Node> nodes;
        private readonly Graph graph;
        private readonly int source,
            sink;
        private readonly int[,] costMatrix;

        public TaskAllocator(List<DataModel.Task> tasks, List<Node> nodes, int[,] costMatrix)
        {
            this.tasks = tasks;
            this.nodes = nodes;
            this.costMatrix = costMatrix;

            int n = 2 + tasks.Count + nodes.Count; // Source + Sink + tasks + nodes
            graph = new Graph(n);

            source = 0;
            sink = n - 1;

            BuildGraph();
        }

        /// <summary>
        /// Build the flow graph:
        /// - Source -> Task (capacity = 1, cost = 0)
        /// - Task -> Node (capacity = 1, cost = execution cost) -- only if node individually can run the task
        /// - Node -> Sink (capacity = node.Slots, cost = 0)
        /// This models per-task assignment (one task to exactly one node).
        /// </summary>
        private void BuildGraph()
        {
            //Source -> Tasks
            for (int i = 0; i < tasks.Count; i++)
            {
                int taskVertex = 1 + i;
                graph.AddEdge(source, taskVertex, 1, 0); // each task can be selected exactly once
            }

            //Tasks -> Nodes
            for (int i = 0; i < tasks.Count; i++)
            {
                int taskVertex = 1 + i;
                for (int j = 0; j < nodes.Count; j++)
                {
                    int nodeVertex = 1 + tasks.Count + j;

                    int cost = costMatrix[i, j];
                    if (cost == int.MaxValue) // treat as ∞
                        continue;

                    graph.AddEdge(taskVertex, nodeVertex, 1, cost);
                }
            }

            //Nodes -> Sink
            for (int j = 0; j < nodes.Count; j++)
            {
                int nodeVertex = 1 + tasks.Count + j;
                // Slots limits number of concurrent tasks on this node
                graph.AddEdge(nodeVertex, sink, nodes[j].Slots, 0);
            }
        }

        /// <summary>
        /// Solve using the MCMF solver and extract allocation (task -> node).
        /// Returns (maxFlow, minCost, allocation-list).
        /// </summary>
        public (int maxFlow, int minCost, List<(DataModel.Task, Node)> allocation) Solve()
        {
            var mcmf = new MinCostMaxFlow.MinCostMaxFlow(graph, source, sink);
            var (flow, cost) = mcmf.GetMinCostMaxFlow();

            var allocation = new List<(DataModel.Task, Node)>();

            // Extract which task -> node edges have positive flow
            for (int i = 0; i < tasks.Count; i++)
            {
                int taskVertex = 1 + i;

                foreach (var e in graph.Adj[taskVertex])
                {
                    if (e.To != source && e.Flow > 0) // assigned edge
                    {
                        int nodeIndex = e.To - (1 + tasks.Count);
                        if (nodeIndex >= 0 && nodeIndex < nodes.Count)
                        {
                            allocation.Add((tasks[i], nodes[nodeIndex]));
                        }
                    }
                }
            }

            return (flow, cost, allocation);
        }

        /// <summary>
        /// Produce Phase-1 style JSON output:
        /// </summary>
        public string GetPhase1OutputJson()
        {
            var (flow, cost, allocation) = Solve();

            var assignments = new Dictionary<string, string>();
            foreach (var (t, n) in allocation)
            {
                assignments[t.Id] = n.Id;
            }

            var outputObj = new { assignments = assignments, total_cost = cost };

            return JsonSerializer.Serialize(
                outputObj,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
    }
}
