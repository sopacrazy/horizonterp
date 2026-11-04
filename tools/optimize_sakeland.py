"""Offline Blender simplification of selected repeated Sakeland test meshes.

Run: blender.exe --background --factory-startup --python tools/optimize_sakeland.py
The source meshbin and manifest are read-only. Coordinates stay in Unity Y-up.
"""
from pathlib import Path
from collections import Counter
import json
import hashlib
import math
import struct
import time

import bpy
import bmesh


ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'tools/sakeland/processed'
OUT = ROOT / 'tools/sakeland/optimized'
RATIOS = {
    'beach_tiki_bar_spotlights': .35,
    'beach_tiki_bar_bamboo_fence': .4,
    'beach_tiki_bar_bamboo_bench': .4,
    'tiki_cycas_palm_01': .45,
    'tiki_cycas_palm_02': .45,
    'tiki_cycas_palm_03': .45,
    'tiki_coco_palm_hammock_01': .45,
    'beach_tiki_bar_bamboo': .35,
}


def read_mesh(path):
    data = path.read_bytes()
    vertices, indices = struct.unpack_from('<ii', data)
    assert vertices > 0 and indices >= 3 and indices % 3 == 0
    assert len(data) == 8 + vertices * 32 + indices * 4, path
    points = list(struct.iter_unpack('<8f', data[8:8 + vertices * 32]))
    triangles = [v[0] for v in struct.iter_unpack('<i', data[8 + vertices * 32:])]
    assert all(0 <= i < vertices for i in triangles)
    return points, triangles


def bounds(points):
    return [[min(p[i] for p in points), max(p[i] for p in points)] for i in range(3)]


def export_mesh(mesh, path):
    mesh.calc_loop_triangles()
    uv_layer = mesh.uv_layers.active.data
    corner_normals = mesh.corner_normals
    packed_vertices, indices, seen, points = [], [], {}, []
    for triangle in mesh.loop_triangles:
        for loop_index in triangle.loops:
            vertex = mesh.vertices[mesh.loops[loop_index].vertex_index]
            uv = uv_layer[loop_index].uv
            normal = corner_normals[loop_index].vector
            values = (*vertex.co, *normal, *uv)
            assert all(math.isfinite(v) for v in values)
            key = struct.pack('<8f', *values)
            index = seen.get(key)
            if index is None:
                index = len(packed_vertices)
                seen[key] = index
                packed_vertices.append(key)
                points.append(values)
            indices.append(index)
    assert indices and len(indices) % 3 == 0
    with path.open('wb') as file:
        file.write(struct.pack('<ii', len(packed_vertices), len(indices)))
        file.writelines(packed_vertices)
        file.write(struct.pack('<' + 'i' * len(indices), *indices))
    # Validate the exact output representation consumed by Unity.
    read_mesh(path)
    return len(packed_vertices), len(indices) // 3, bounds(points)


def optimize(geometry, ratio):
    name = geometry['mesh']
    points, indices = read_mesh(SOURCE / (name + '.meshbin'))
    before_bounds = bounds(points)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([p[:3] for p in points], [],
                     [indices[i:i+3] for i in range(0, len(indices), 3)])
    uv_layer = mesh.uv_layers.new(name='UVMap')
    for loop in mesh.loops:
        uv_layer.data[loop.index].uv = points[loop.vertex_index][6:8]
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    # Merge coincident vertex positions but retain UVs per face corner.
    # The exported mesh splits vertices again at differing UVs and corner normals.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-5)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    mesh.set_sharp_from_angle(angle=math.radians(40))
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    before_triangles = len(indices) // 3
    # Very small details keep their original topology instead of disappearing.
    if before_triangles >= 48:
        modifier = obj.modifiers.new(name='MobileDecimate', type='DECIMATE')
        modifier.decimate_type = 'COLLAPSE'
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    mesh = obj.data
    # Recompute corner normals from the simplified topology, preserving hard edges.
    mesh.set_sharp_from_angle(angle=math.radians(40))
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    mesh.update()
    output = OUT / (name + '.meshbin')
    after_vertices, after_triangles, after_bounds = export_mesh(mesh, output)
    max_extent_loss = max(
        abs((b[1]-b[0])-(a[1]-a[0])) / max(.01, b[1]-b[0])
        for b, a in zip(before_bounds, after_bounds)
    )
    max_boundary_shift = max(abs(bv-av) for before,after in zip(before_bounds,after_bounds)
                             for bv,av in zip(before,after))
    fallback_reason = None
    # Thin/sparse disconnected pieces may disappear under collapse. Preserve the
    # original geometry if its outer shape changes substantially.
    if max_extent_loss > .1 and max_boundary_shift > .08:
        output.write_bytes((SOURCE / (name + '.meshbin')).read_bytes())
        fallback_reason = 'Original preserved: simplification changed outer bounds by more than 10% and 8 cm.'
        after_vertices, after_triangles, after_bounds = len(points), before_triangles, before_bounds
        max_extent_loss = max_boundary_shift = 0
    result = {
        'mesh': name, 'verticesBefore': len(points), 'verticesAfter': after_vertices,
        'trianglesBefore': before_triangles, 'trianglesAfter': after_triangles,
        'ratioRequested': ratio, 'ratioActual': after_triangles / before_triangles,
        'boundsBefore': before_bounds, 'boundsAfter': after_bounds,
        'maxExtentChangeFraction': max_extent_loss,
        'maxBoundaryShiftMetres': max_boundary_shift,
        'fallbackReason': fallback_reason,
        'sourceSha256': hashlib.sha256((SOURCE / (name + '.meshbin')).read_bytes()).hexdigest(),
        'uvLayers': len(mesh.uv_layers),
    }
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.meshes.remove(mesh)
    print('OPTIMIZED', name, before_triangles, '->', after_triangles, flush=True)
    return result


def main():
    started = time.monotonic()
    OUT.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    manifest = json.loads((SOURCE / 'manifest.json').read_text(encoding='utf-8'))
    instances = Counter(p['name'] for p in manifest['placements'])
    models, geometry_metrics = [], []
    for model in manifest['models']:
        if model['name'] not in RATIOS:
            continue
        metrics = [optimize(g, RATIOS[model['name']]) for g in model['geometries']]
        geometry_metrics.extend(metrics)
        before = sum(m['trianglesBefore'] for m in metrics)
        after = sum(m['trianglesAfter'] for m in metrics)
        count = instances[model['name']]
        models.append({'name': model['name'], 'instances': count,
                       'trianglesBefore': before, 'trianglesAfter': after,
                       'instancedTrianglesBefore': before*count,
                       'instancedTrianglesAfter': after*count})
    assert {m['name'] for m in models} == set(RATIOS), 'Selected model missing'
    total_before = sum(m['instancedTrianglesBefore'] for m in models)
    total_after = sum(m['instancedTrianglesAfter'] for m in models)
    all_before = sum(sum(g['triangles'] for g in m['geometries'])*instances[m['name']]
                     for m in manifest['models'])
    all_after = all_before-total_before+total_after
    report = {'blender': bpy.app.version_string, 'seconds': time.monotonic()-started,
              'sourceUnchanged': True, 'coordinateSpace': 'Unity Y-up preserved',
              'models': models, 'geometries': geometry_metrics,
              'selectedInstancedTrianglesBefore': total_before,
              'selectedInstancedTrianglesAfter': total_after,
              'selectedReductionFraction': 1-total_after/total_before,
              'wholeSceneInstancedTrianglesBefore': all_before,
              'wholeSceneInstancedTrianglesAfter': all_after,
              'wholeSceneReductionFraction': 1-all_after/all_before,
              'limits': ['Geometry and UV continuity require visual review in Unity.',
                         'Recomputed normals use 40-degree hard edges.',
                         'No textures, collision meshes, or unselected models are modified.']}
    (OUT / 'metrics.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('models', 'geometries')}, indent=2), flush=True)
    assert total_after <= total_before*.5, 'Selected instanced triangle reduction below 50 percent'


if __name__ == '__main__':
    main()
