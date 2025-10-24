let scene, camera, renderer;
let lines = [];
let vlines = [];
let linesOrg = [];
let smooth = false;      // toggle this on/off
let smoothLines = [];

// smoother, more rounded tube:
const subdivisions = 60;
const tension = 0.3;
const radialSegs = 12;


let nodes = [];
let VecteursPropres = [];
let t = 0;
let mode = 5;

let centerX =0;
let centerY =0;
let centerZ =0;
let scaleFactor = 0;

let controls;

function initThreeJsCanvas(canvas) {
    let width = canvas.clientWidth;
    let height = canvas.clientHeight;
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0xEEEEEE);

    const ambient = new THREE.AmbientLight(0xffffff, 0.4);
    scene.add(ambient);

    const axes = new THREE.AxesHelper(2);   // length of each axis line
    scene.add(axes);

    camera = new THREE.PerspectiveCamera(75, width / height, 0.1, 1000);
    renderer = new THREE.WebGLRenderer({ canvas: canvas });
    renderer.setSize(width, height);

    camera.position.z = 5;
    camera.rotation.x = 0.2; // Rotate camera slightly around X axis
    camera.rotation.y = 0.3; // Rotate camera slightly around Y axis

    controls = new THREE.OrbitControls(camera, renderer.domElement);
    animate();
}

function setMode(m) {
    mode = m;
}

function drawLine(x1, y1, z1, x2, y2, z2, width =1, height=1) {
    x1 = (x1 - centerX) * scaleFactor;
    y1 = (y1 - centerY) * scaleFactor;
    z1 = (z1 - centerZ) * scaleFactor;

    x2 = (x2 - centerX) * scaleFactor;
    y2 = (y2 - centerY) * scaleFactor;
    z2 = (z2 - centerZ) * scaleFactor;

    const dx = x2 - x1;
    const dy = y2 - y1;
    const dz = z2 - z1;

    const length = Math.sqrt(dx * dx + dy * dy + dz * dz);
    const direction = new THREE.Vector3(dx, dy, dz).normalize();

    const geometry = new THREE.BufferGeometry();

    const vertices = new Float32Array([
        // Front face
        -width / 2, -height / 2, 0,
        width / 2, -height / 2, 0,
        width / 2, height / 2, 0,
        -width / 2, height / 2, 0,

        // Back face
        -width / 2, -height / 2, length,
        width / 2, -height / 2, length,
        width / 2, height / 2, length,
        -width / 2, height / 2, length,
    ]);

    const indices = new Uint16Array([
        0, 1, 2, 0, 2, 3,
        4, 5, 6, 4, 6, 7,
        0, 1, 5, 0, 5, 4,
        2, 3, 7, 2, 7, 6,
        0, 3, 7, 0, 7, 4,
        1, 2, 6, 1, 6, 5
    ]);

    geometry.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));

    const material = new THREE.MeshBasicMaterial({ color: 0x0000ff, wireframe: true });
    const mesh = new THREE.Mesh(geometry, material);

    const quaternion = new THREE.Quaternion();
    quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), direction);
    mesh.quaternion.copy(quaternion);

    mesh.position.set((x1 + x2) / 2, (y1 + y2) / 2, (z1 + z2) / 2);

    return mesh;
}


function drawLinedgfdg(x1, y1, z1, x2, y2, z2, width=1, height=1) {
    const dx = x2 - x1;
    const dy = y2 - y1;
    const dz = z2 - z1;
    const length = Math.sqrt(dx * dx + dy * dy + dz * dz);

    // Define the vertices for the beam
    const halfWidth = width / 2;
    const halfHeight = height / 2;
    const vertices = new Float32Array([
        -halfWidth, -halfHeight, 0,
        halfWidth, -halfHeight, 0,
        halfWidth, halfHeight, 0,
        -halfWidth, halfHeight, 0,
        -halfWidth, -halfHeight, length,
        halfWidth, -halfHeight, length,
        halfWidth, halfHeight, length,
        -halfWidth, halfHeight, length,
    ]);

    // Define the indices for the beam
    const indices = new Uint16Array([
        0, 1, 2, 0, 2, 3,  // Front face
        4, 5, 6, 4, 6, 7,  // Back face
        0, 1, 5, 0, 5, 4,  // Bottom face
        2, 3, 7, 2, 7, 6,  // Top face
        0, 3, 7, 0, 7, 4,  // Left face
        1, 2, 6, 1, 6, 5,  // Right face
    ]);

    // Create the geometry
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));

    // Create the material
    const material = new THREE.MeshBasicMaterial({ color: 0x0000ff });

    // Create the mesh
    const mesh = new THREE.Mesh(geometry, material);

    // Set the position and orientation of the mesh
    const position = new THREE.Vector3((x1 + x2) / 2, (y1 + y2) / 2, (z1 + z2) / 2);
    mesh.position.copy(position);
    const axis = new THREE.Vector3(dx, dy, dz).normalize();
    const up = new THREE.Vector3(0, 0, 1);
    const quaternion = new THREE.Quaternion();
    quaternion.setFromUnitVectors(up, axis);
    mesh.setRotationFromQuaternion(quaternion);

    return mesh;
}

function drawLineold(x1, y1, z1, x2, y2, z2) {
    x1 = (x1 - centerX) * scaleFactor;
    y1 = (y1 - centerY) * scaleFactor;
    z1 = (z1 - centerZ) * scaleFactor;

    x2 = (x2 - centerX) * scaleFactor;
    y2 = (y2 - centerY) * scaleFactor;
    z2 = (z2 - centerZ) * scaleFactor;

    const material = new THREE.LineBasicMaterial({ color: 0x0000ff });
    const points = [];
    points.push(new THREE.Vector3(x1, y1, z1));
    points.push(new THREE.Vector3(x2, y2, z2));
    const geometry = new THREE.BufferGeometry().setFromPoints(points);
    return new THREE.Line(geometry, material);
}

function drawLineoo(x1, y1, z1, x2, y2, z2) {
    const dx = x2 - x1;
    const dy = y2 - y1;
    const dz = z2 - z1;
    const length = Math.sqrt(dx * dx + dy * dy + dz * dz);

    // Define the 2D shape that will be extruded
    const shape = new THREE.Shape();
    shape.moveTo(-0.05, -0.05);
    shape.lineTo(0.05, -0.05);
    shape.lineTo(0.05, 0.05);
    shape.lineTo(-0.05, 0.05);
    shape.lineTo(-0.05, -0.05);

    // Define the extrusion settings
    const extrudeSettings = {
        steps: 1,
        depth: length,
        bevelEnabled: false,
    };

    // Create the geometry by extruding the shape
    const geometry = new THREE.ExtrudeGeometry(shape, extrudeSettings);

    // Create the material
    const material = new THREE.MeshBasicMaterial({ color: 0x0000ff });

    // Create the mesh
    const mesh = new THREE.Mesh(geometry, material);

    // Set the position of the mesh to be the midpoint of the line
    const position = new THREE.Vector3((x1 + x2) / 2, (y1 + y2) / 2, (z1 + z2) / 2);
    mesh.position.copy(position);

    // Set the orientation of the mesh to match the direction of the line
    const axis = new THREE.Vector3(dx, dy, dz).normalize();
    const up = new THREE.Vector3(0, 0, 1);
    const quaternion = new THREE.Quaternion();
    quaternion.setFromUnitVectors(up, axis);
    mesh.setRotationFromQuaternion(quaternion);

    return mesh;
}


function drawLineo(x1, y1, z1, x2, y2, z2) {
    x1 = (x1 - centerX) * scaleFactor;
    y1 = (y1 - centerY) * scaleFactor;
    z1 = (z1 - centerZ) * scaleFactor;

    x2 = (x2 - centerX) * scaleFactor;
    y2 = (y2 - centerY) * scaleFactor;
    z2 = (z2 - centerZ) * scaleFactor;

    const dx = x2 - x1;
    const dy = y2 - y1;
    const dz = z2 - z1;
    const length = Math.sqrt(dx * dx + dy * dy + dz * dz);

    const material = new THREE.MeshBasicMaterial({ color: 0x0000ff });
    const geometry = new THREE.BoxGeometry(0.1, 0.1, length); // Adjust the width and height here
    const beam = new THREE.Mesh(geometry, material);

    beam.position.set((x1 + x2) / 2, (y1 + y2) / 2, (z1 + z2) / 2);
    beam.lookAt(new THREE.Vector3(x2, y2, z2));

    return beam;
}

function createSmoothEntry(coords, nodePair, subdivisions = 20, radius = 0.02) {
    // center & scale endpoints exactly as your other drawLine* funcs do
    let [x1, y1, z1, x2, y2, z2] = coords.map((v, i) =>
        (v - [centerX, centerY, centerZ][i % 3]) * scaleFactor
    );
    const A = new THREE.Vector3(x1, y1, z1);
    const B = new THREE.Vector3(x2, y2, z2);
    const mid = new THREE.Vector3().addVectors(A, B).multiplyScalar(0.5);

   // const curve = new THREE.CatmullRomCurve3([A, mid, B], false, 'catmullrom', 0.5);
    const curve = new THREE.CatmullRomCurve3([A, mid, B], false, 'catmullrom', tension);


    let mesh;
    if (radius > 0) {

        const geom = new THREE.TubeGeometry(
            curve,
            subdivisions,    // more subdivisions = smoother path
            0.02,            // beam radius
            radialSegs,      // more radial segments = smoother circumference
            false
        );
        const mat = new THREE.MeshBasicMaterial({ color: 0x0000ff });
        mesh = new THREE.Mesh(geom, mat);



    } else {
        const pts = curve.getPoints(subdivisions);
        const geom = new THREE.BufferGeometry().setFromPoints(pts);
        mesh = new THREE.Line(geom, new THREE.LineBasicMaterial({ color: 0x0000ff }));
    }

    return { mesh, curve, subdivisions, radius, coords, nodePair };
}

function addLinesToScene(linesList, nodesList, VecteursPropresJagged, doSmooth = false) {
    smooth = doSmooth;
    let minX = Infinity, minY = Infinity, minZ = Infinity;
    let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;

    for (let lineCoords of linesList) {
        for (let i = 0; i < 6; i += 3) {
            if (lineCoords[i] < minX) minX = lineCoords[i];
            if (lineCoords[i + 1] < minY) minY = lineCoords[i + 1];
            if (lineCoords[i + 2] < minZ) minZ = lineCoords[i + 2];

            if (lineCoords[i] > maxX) maxX = lineCoords[i];
            if (lineCoords[i + 1] > maxY) maxY = lineCoords[i + 1];
            if (lineCoords[i + 2] > maxZ) maxZ = lineCoords[i + 2];
        }
    }

    centerX = 0;// (minX + maxX) / 2;
    centerY = 0;// (minY + maxY) / 2;
    centerZ = 0;// (minZ + maxZ) / 2;

    let rangeX = maxX - minX;
    let rangeY = maxY - minY;
    let rangeZ = maxZ - minZ;

    scaleFactor = 1; // 2 / Math.max(rangeX, rangeY, rangeZ);  // Assuming you want your objects to occupy a range of 2 units (for example)


    VecteursPropres = VecteursPropresJagged;
    nodes = nodesList;

    for (let idx = 0; idx < linesList.length; idx++) {
        const coords = linesList[idx];
        const nodePair = nodesList[idx];

        if (smooth) {
            // build & stash a dynamic spline entry
            const entry = createSmoothEntry(coords, nodePair);
            smoothLines.push(entry);
            scene.add(entry.mesh);

            const b = drawLineo(...coords);
            scene.add(b);

            const lineOrg = drawLineold(...coords);
            linesOrg.push(lineOrg);

            const linev = drawLineold(
                coords[0], coords[1], coords[2],
                coords[0], coords[1] + 1, coords[2]
            );
            vlines.push(linev);
            scene.add(linev);

        } else {
            // your original straight‐line + beam + vertical‐line code:
            const line = drawLineold(...coords);
            lines.push(line);
            scene.add(line);

            const b = drawLineo(...coords);
            scene.add(b);

            const lineOrg = drawLineold(...coords);
            linesOrg.push(lineOrg);

            const linev = drawLineold(
                coords[0], coords[1], coords[2],
                coords[0], coords[1] + 1, coords[2]
            );
            vlines.push(linev);
            scene.add(linev);
        }
    }
}

function animate() {
    // 1) schedule next frame
    requestAnimationFrame(animate);

    // 2) update controls (for damping/auto-rotate)
    controls.update();

    // 3) update our time & compute deformation scale
    t += 0.01;
    const scaleAnim = 200 * Math.sin(t);

    if (smooth) {
        // --- Dynamic smoothing path ---
        for (let entry of smoothLines) {
            // a) recompute endpoints with modal deformation
            let [x1, y1, z1, x2, y2, z2] = entry.coords.map((v, i) =>
                (v - [centerX, centerY, centerZ][i % 3]) * scaleFactor
            );

            // apply modal offsets in X/Y
            const n0 = entry.nodePair[0] * 3;
            const n1 = entry.nodePair[1] * 3;
            //x1 += VecteursPropres[n0 + 0][mode] * scaleAnim;
            y1 += VecteursPropres[n0 + 1][mode] * scaleAnim;
            //x2 += VecteursPropres[n1 + 0][mode] * scaleAnim;
            y2 += VecteursPropres[n1 + 1][mode] * scaleAnim;
            // (keep z at 0 if your deformation is planar)

            const A = new THREE.Vector3(x1, y1, 0);
            const B = new THREE.Vector3(x2, y2, 0);
            const mid = new THREE.Vector3().addVectors(A, B).multiplyScalar(0.5);

            // b) update control points on the curve
            entry.curve.points = [A, mid, B];

            // c) resample or rebuild geometry
            if (entry.radius > 0) {
                // tube: dispose old and recreate
                entry.mesh.geometry.dispose();
                entry.mesh.geometry = new THREE.TubeGeometry(
                    entry.curve,
                    entry.subdivisions,
                    entry.radius,
                    8,
                    false
                );
            } else {
                // thin line: update positions buffer
                const pts = entry.curve.getPoints(entry.subdivisions);
                entry.mesh.geometry.setFromPoints(pts);
                entry.mesh.geometry.attributes.position.needsUpdate = true;
            }
        }

    } else {
        // --- Original jagged‐line path ---
        let idx = 0;
        for (let line of lines) {
            const vertices = line.geometry.attributes.position.array;
            const verticesOrg = linesOrg[idx].geometry.attributes.position.array;

            // endpoint A
            vertices[0] = verticesOrg[0] + VecteursPropres[nodes[idx][0] * 3 + 0][mode] * scaleAnim;
            vertices[1] = verticesOrg[1] + VecteursPropres[nodes[idx][0] * 3 + 1][mode] * scaleAnim;
            vertices[2] = 0;

            // endpoint B
            vertices[3] = verticesOrg[3] + VecteursPropres[nodes[idx][1] * 3 + 0][mode] * scaleAnim;
            vertices[4] = verticesOrg[4] + VecteursPropres[nodes[idx][1] * 3 + 1][mode] * scaleAnim;
            vertices[5] = 0;

            line.geometry.attributes.position.needsUpdate = true;
            idx++;
        }
    }

    // 4) finally, render!
    renderer.render(scene, camera);
}

window.threeJsFunctions = {
    initCanvas: initThreeJsCanvas,
    addLines: addLinesToScene
};
