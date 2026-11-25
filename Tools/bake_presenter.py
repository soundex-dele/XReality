"""Bake the XReality bust from CC0 MakeHuman/MPFB data (not addon code).
Usage: python Tools/bake_presenter.py MPFB_ARCHIVE CC0_SYSTEM_ASSETS_ARCHIVE
Requires NumPy and Pillow; Unity builds use the checked-in outputs offline.
"""
import gzip
import hashlib
from io import BytesIO
from pathlib import Path
import struct
import sys
from zipfile import ZipFile

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/XReality/Presenter'
MPFB = 'e8103012013c829f6c9a0972b7ccd0a2f56b0596aa6f717d535e0bea79cd8910'
SYSTEM = 'b542127a8e25547c7c29c19f2d1d2adb9a664c80396ecd694095dbc8028a0107'
TARGETS = ['eye-left-closure', 'eye-right-closure', 'mouth-open',
           'mouth-corner-puller', 'eyebrows-left-up', 'eyebrows-right-up',
           'mouth-pursing', 'mouth-protusion', 'mouth-compression']
SCALE = np.array([.61, .61, -.61])
ORIGIN = np.array([0, 4.45, .68])


def checked(path, expected):
    with open(path, 'rb') as stream:
        sha = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            sha.update(chunk)
    if sha.hexdigest() != expected:
        raise ValueError(f'Unrecognized upstream archive: {path}')
    return ZipFile(path)


def lines(data):
    for row in data.decode('utf-8').splitlines():
        tokens = row.partition('#')[0].split()
        if tokens:
            yield tokens


def obj(data):
    points, texcoords, faces = [], [], []
    group = ''
    for tag, *values in lines(data):
        if tag == 'v':
            points.append(list(map(float, values[:3])))
        elif tag == 'vt':
            texcoords.append(list(map(float, values[:2])))
        elif tag == 'g':
            group = values[0]
        elif tag == 'f':
            faces.append((group, [tuple(int(x) - 1 for x in pair.split('/')[:2]) for pair in values]))
    return np.array(points), np.array(texcoords), faces


def target(archive, name, count):
    result = np.zeros((count, 3))
    for index, x, y, z in lines(gzip.decompress(archive.read('mpfb/data/targets/' + name + '.target.gz'))):
        result[int(index)] = float(x), float(y), float(z)
    return result


def proxy(archive, path, base, deltas):
    """MHCLO maps each accessory vertex to its fitted base references and offset."""
    source, uv, faces = obj(archive.read(path + '.obj'))
    coords, deforms, hidden = [], [], set()
    axes = np.ones(3)
    mode = None
    for tag, *values in lines(archive.read(path + '.mhclo')):
        if tag in ('x_scale', 'y_scale', 'z_scale'):
            axis = 'xyz'.index(tag[0])
            a, b, ref = values
            axes[axis] = abs(base[int(a), axis] - base[int(b), axis]) / float(ref)
        elif tag in ('verts', 'delete_verts'):
            mode = tag
        elif mode == 'verts':
            row = [tag, *values]
            if len(row) == 1:
                indices, weights, offset = [int(tag)], [1.], [0., 0., 0.]
            elif len(row) == 9:
                indices, weights, offset = list(map(int, row[:3])), list(map(float, row[3:6])), list(map(float, row[6:]))
            else:
                raise ValueError(f'Unsupported proxy row: {path} {row}')
            coords.append(np.asarray(weights) @ base[indices] + axes * offset)
            deforms.append([np.asarray(weights) @ delta[indices] for delta in deltas])
        elif mode == 'delete_verts':
            row = iter([tag, *values]); previous = None
            for token in row:
                if token == '-':
                    end = int(next(row)); hidden.update(range(previous, end + 1)); previous = end
                else:
                    previous = int(token); hidden.add(previous)
    if len(coords) != len(source):
        raise ValueError(f'Unexpected proxy topology: {path}')
    return np.array(coords), uv, faces, np.transpose(deforms, (1, 0, 2)), hidden


def mesh(path, source, uv, faces, deltas=(), blend='head'):
    positions = (source - ORIGIN) * SCALE
    corners, indices = {}, []
    for _, polygon in faces:
        face = [corners.setdefault(key, len(corners)) for key in polygon]
        for j in range(1, len(face) - 1):
            indices.extend([face[0], face[j + 1], face[j]])  # reflected Z
    vi, uvi = np.array(list(corners)).T
    tris = np.array(indices, dtype='<u4').reshape((-1, 3))
    source_tris = vi[tris]

    def normals(vertices):
        normals = np.zeros_like(vertices)
        a, b, c = vertices[source_tris].transpose(1, 0, 2)
        cross = np.cross(b - a, c - a)
        for column in source_tris.T:
            np.add.at(normals, column, cross)
        mag = np.linalg.norm(normals, axis=1)
        return normals / np.maximum(1e-15, mag[:, None])

    normal = normals(positions)
    # Jaw and oral meshes must stay together; blend only down the neck.
    factor = np.clip((source[vi, 1] - 5.70) / .44, 0, 1)
    weight = factor * factor * (3 - 2 * factor) if blend == 'skin' else np.full(len(vi), blend == 'head')

    def floats(*columns):
        return np.column_stack(columns).astype('<f4').tobytes()

    with open(path, 'wb') as stream:
        stream.write(struct.pack('<4sIII', b'XRP1', len(vi), len(indices), len(deltas)))
        stream.write(floats(positions[vi], normal[vi], uv[uvi], weight))
        stream.write(tris.tobytes())
        for delta in deltas:
            offset = delta * SCALE
            stream.write(floats(offset[vi], (normals(positions + offset) - normal)[vi]))
    print(path.name, len(vi), len(tris), 'triangles')


def slice_above(source, uv, faces, floor):
    """Clip the home bust at its pedestal, interpolating UVs along each cut edge."""
    points, coords, clipped, new_points, new_uvs = list(source), list(uv), [], {}, {}

    def intersect(a, b):
        i, j = a[0], b[0]
        k, h = a[1], b[1]
        t = (floor - source[i, 1]) / (source[j, 1] - source[i, 1])
        edge = tuple(sorted((i, j)))
        if edge not in new_points:
            new_points[edge] = len(points)
            points.append(source[i] + t * (source[j] - source[i]))
        # The same geometry edge may have different UVs on either side of a seam.
        tex_edge = tuple(sorted((a, b)))
        if tex_edge not in new_uvs:
            new_uvs[tex_edge] = len(coords)
            coords.append(uv[k] + t * (uv[h] - uv[k]))
        return new_points[edge], new_uvs[tex_edge]

    for group, polygon in faces:
        keep = []
        previous = polygon[-1]
        for current in polygon:
            above = source[current[0], 1] >= floor
            if above != (source[previous[0], 1] >= floor):
                keep.append(intersect(previous, current))
            if above:
                keep.append(current)
            previous = current
        if len(keep) > 2:
            clipped.append((group, keep))
    return np.array(points), np.array(coords), clipped


def texture(archive, name, output, size, alpha=False):
    with Image.open(BytesIO(archive.read(name))) as source:
        image = source.convert('RGBA' if alpha else 'RGB')
        image.thumbnail((size, size), Image.Resampling.LANCZOS)
        image.save(OUT / (output + '.png'), optimize=True)


def bake(mpfb, system):
    OUT.mkdir(parents=True, exist_ok=True)
    base, uv, faces = obj(mpfb.read('mpfb/data/3dobjs/base.obj'))
    base += target(mpfb, 'macrodetails/asian-male-young', len(base))
    blink_l, blink_r, jaw, smile, brow_l, brow_r, purse, protrude, press = [
        target(mpfb, 'expression/units/asian/' + name, len(base)) for name in TARGETS]
    # Split the symmetric smile with complementary weights across the lip center.
    # Both sides at 100 percent reconstruct the original shape, not twice the motion.
    side = np.clip((base[:, 0] + .20) / .40, 0, 1)[:, None]
    side = side * side * (3 - 2 * side)
    # Limit protrusion and compression so the round shape can still blend with jaw opening.
    deltas = np.stack([blink_l, blink_r, jaw, smile * side, smile * (1 - side), brow_l, brow_r,
                       .85 * purse + .22 * protrude, .85 * press - .09 * jaw])
    outfit = 'clothes/male_elegantsuit01/male_elegantsuit01'
    jacket, juv, jfaces, _, hide = proxy(system, outfit, base, deltas)
    above = lambda coords, face, minimum: min(coords[i, 1] for i, _ in face) >= minimum
    skin = [(group, face) for group, face in faces if group == 'body' and above(base, face, 4.20)
            and not all(i in hide for i, _ in face)]
    mesh(OUT / 'Skin.bytes', base, uv, skin, deltas, 'skin')
    mesh(OUT / 'Jacket.bytes', jacket, juv, [(g, f) for g, f in jfaces if above(jacket, f, 3.20)], blend='torso')
    mesh(OUT / 'JacketShowcase.bytes', *slice_above(jacket, juv, jfaces, 4.78), blend='torso')
    texture(system, 'skins/young_asian_male/young_lightskinned_male_diffuse3.png', 'Skin', 2048)
    texture(system, outfit + '_diffuse.png', 'Jacket', 1024)
    for name, source, tex, alpha, channels in [
            ('Hair', 'hair/short02/short02', 'hair/short02/short02_diffuse.png', True, ()),
            ('Brows', 'eyebrows/eyebrow005/eyebrow005', 'eyebrows/eyebrow005/eyebrow005.png', True, (5, 6)),
            ('Eyes', 'eyes/low-poly/low-poly', 'eyes/materials/brown_eye.png', False, ()),
            ('Teeth', 'teeth/teeth_base/teeth_base', 'teeth/teeth_base/teeth.png', False, (2,))]:
        points, uvs, polygons, offsets, _ = proxy(system, source, base, deltas)
        mesh(OUT / (name + '.bytes'), points, uvs, polygons, offsets[list(channels)])
        texture(system, tex, name, 1024 if name == 'Hair' else 512, alpha)


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    with checked(sys.argv[1], MPFB) as mpfb, checked(sys.argv[2], SYSTEM) as system:
        bake(mpfb, system)
