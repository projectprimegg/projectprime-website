from __future__ import annotations
import sys
from pathlib import Path


def replace_once(path: Path, before: str, after: str, label: str) -> None:
    text = path.read_text(encoding='utf-8')
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f'{label}: expected one anchor, found {count} in {path}')
    path.write_text(text.replace(before, after, 1), encoding='utf-8', newline='\n')


def main() -> int:
    if len(sys.argv) != 2:
        raise SystemExit('usage: apply_fine_traversal_graph.py <prime-root>')
    root = Path(sys.argv[1]).resolve()
    path = root / 'src/MphRead/Mods/MapGen/MapNodePacker.cs'
    anchor = '''        public sealed record NavigationGraph(byte[] Bytes, Vector3[] Positions, int[][] Neighbours, int[] Components, int Edges)
        {
            public NodeType[] Types { get; init; } = Array.Empty<NodeType>();
        }

        public static NavigationGraph Analyze(IReadOnlyList<BuiltFace> solid, IReadOnlyList<MapNavigationLink>? links = null, CancellationToken cancellation = default)
'''
    replacement = '''        public sealed record NavigationGraph(byte[] Bytes, Vector3[] Positions, int[][] Neighbours, int[] Components, int Edges)
        {
            public NodeType[] Types { get; init; } = Array.Empty<NodeType>();
        }

        /// <summary>
        /// Return the pre-decimation collision traversal graph. Runtime navigation
        /// remains bounded to 700 nodes, but collision certification must follow the
        /// actual short walk steps instead of drawing unsafe straight lines between
        /// coarse routing representatives on opposite sides of a wall or bend.
        /// </summary>
        public static NavigationGraph AnalyzeFineForCertification(
            IReadOnlyList<BuiltFace> solid,
            CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            List<Node> fine = Sample(solid, FineSpacing, cancellation);
            Connect(solid, fine, FineSpacing, cancellation);
            if (fine.Count == 0)
                throw new MapAuthoringException("FP-MAP-007", "No walkable collision floor was found for player-form certification.");
            if (fine.Count > 100000)
                throw new MapAuthoringException("FP-MAP-007", "Fine collision traversal exceeds the 100,000-node certification safety limit.");

            int[] components = Enumerable.Repeat(-1, fine.Count).ToArray();
            var adjacent = fine.Select(node => node.Neighbours.ToHashSet()).ToArray();
            for (int a = 0; a < fine.Count; a++)
                foreach (int b in fine[a].Neighbours)
                    adjacent[b].Add(a);
            int component = 0;
            var queue = new Queue<int>();
            for (int i = 0; i < fine.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (components[i] >= 0) continue;
                queue.Clear();
                queue.Enqueue(i);
                components[i] = component;
                while (queue.TryDequeue(out int current))
                    foreach (int neighbour in adjacent[current])
                        if (components[neighbour] < 0)
                        {
                            components[neighbour] = component;
                            queue.Enqueue(neighbour);
                        }
                component++;
            }
            int edges = fine.Sum(node => node.Neighbours.Count);
            return new NavigationGraph(Array.Empty<byte>(),
                fine.Select(node => node.Position).ToArray(),
                fine.Select(node => node.Neighbours.ToArray()).ToArray(),
                components, edges)
            {
                Types = fine.Select(node => node.Type).ToArray()
            };
        }

        public static NavigationGraph Analyze(IReadOnlyList<BuiltFace> solid, IReadOnlyList<MapNavigationLink>? links = null, CancellationToken cancellation = default)
'''
    replace_once(path, anchor, replacement, 'fine traversal certification graph')
    print('Fine traversal certification graph integrated into', root)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
