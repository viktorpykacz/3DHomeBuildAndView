import * as THREE from 'three';
import { GLTFLoader } from './lib/GLTFLoader.js';

// ============================================================================
// 1. BIM DOMAIN MODEL & COMMAND HISTORY MANAGER (Undo/Redo Ctrl+Z / Ctrl+Y)
// ============================================================================
let uidCounter = 1000;
const nextId = (prefix = 'id') => `${prefix}_${++uidCounter}_${Math.random().toString(36).slice(2, 6)}`;

class CommandHistoryManager {
    constructor(maxSize = 100) {
        this.undoStack = [];
        this.redoStack = [];
        this.maxSize = maxSize;
    }
    pushSnapshot(description, stateBeforeJson, stateAfterJson, restoreCallback) {
        this.undoStack.push({ description, stateBeforeJson, stateAfterJson, restoreCallback });
        if (this.undoStack.length > this.maxSize) this.undoStack.shift();
        this.redoStack = [];
        updateUndoRedoUI();
    }
    undo() {
        if (this.undoStack.length === 0) return;
        const cmd = this.undoStack.pop();
        this.redoStack.push(cmd);
        cmd.restoreCallback(cmd.stateBeforeJson);
        updateUndoRedoUI();
        setTelemetry(`Cofnięto: ${cmd.description}`);
    }
    redo() {
        if (this.redoStack.length === 0) return;
        const cmd = this.redoStack.pop();
        this.undoStack.push(cmd);
        cmd.restoreCallback(cmd.stateAfterJson);
        updateUndoRedoUI();
        setTelemetry(`Ponowiono: ${cmd.description}`);
    }
    clear() {
        this.undoStack = [];
        this.redoStack = [];
        updateUndoRedoUI();
    }
}

const commandHistory = new CommandHistoryManager();
const gltfLoader = new GLTFLoader();
const customGltfCache = new Map(); // assetId -> normalized THREE.Group template

function createStarterProject() {
    const wSW = { id: 'w_sw', start: { x: -3.2, z: -2.2 }, end: { x: 0.4, z: -2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [
        { id: 'op_win1', type: 'WINDOW', dist: 1.8, width: 1.40, height: 1.50, sill: 0.85, flip: false }
    ], mep: [] };
    const wSE = { id: 'w_se', start: { x: 0.4, z: -2.2 }, end: { x: 3.2, z: -2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [
        { id: 'op_door_main', type: 'DOOR', dist: 1.4, width: 0.95, height: 2.05, sill: 0.0, flip: false }
    ], mep: [
        { id: 'mep_sw_main', category: 'SWITCH_SINGLE', dist: 0.65, elev: 1.10, worldPos: { x: 1.05, y: 1.10, z: -2.07 }, normal: { x: 0, y: 0, z: 1 } }
    ] };
    const wE = { id: 'w_e', start: { x: 3.2, z: -2.2 }, end: { x: 3.2, z: 2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [], mep: [
        { id: 'mep_sock_blat1', category: 'SOCKET_230', dist: 1.60, elev: 1.10, worldPos: { x: 3.07, y: 1.10, z: -0.60 }, normal: { x: -1, y: 0, z: 0 } },
        { id: 'mep_water_c', category: 'WATER_COLD', dist: 3.00, elev: 0.55, worldPos: { x: 3.07, y: 0.55, z: 0.80 }, normal: { x: -1, y: 0, z: 0 } },
        { id: 'mep_water_h', category: 'WATER_HOT', dist: 2.80, elev: 0.55, worldPos: { x: 3.07, y: 0.55, z: 0.60 }, normal: { x: -1, y: 0, z: 0 } },
        { id: 'mep_sewage', category: 'SEWAGE_50', dist: 2.90, elev: 0.42, worldPos: { x: 3.07, y: 0.42, z: 0.70 }, normal: { x: -1, y: 0, z: 0 } }
    ] };
    const wNE = { id: 'w_ne', start: { x: 3.2, z: 2.2 }, end: { x: 0.4, z: 2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [], mep: [] };
    const wNW = { id: 'w_nw', start: { x: 0.4, z: 2.2 }, end: { x: -3.2, z: 2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [
        { id: 'op_win2', type: 'WINDOW', dist: 1.8, width: 1.40, height: 1.50, sill: 0.85, flip: false }
    ], mep: [] };
    const wW = { id: 'w_w', start: { x: -3.2, z: 2.2 }, end: { x: -3.2, z: -2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [], mep: [
        { id: 'mep_sock_sofa', category: 'SOCKET_230', dist: 0.85, elev: 0.30, worldPos: { x: -3.07, y: 0.30, z: 1.35 }, normal: { x: 1, y: 0, z: 0 } }
    ] };
    const wPart = { id: 'w_part', start: { x: 0.4, z: -2.2 }, end: { x: 0.4, z: 2.2 }, thickness: 0.12, height: 2.75, clipRoof: false, openings: [
        { id: 'op_door_int', type: 'DOOR', dist: 2.2, width: 0.90, height: 2.05, sill: 0.0, flip: false }
    ], mep: [] };

    const innerEastX = 3.2 - 0.12;
    const innerWestX = -3.2 + 0.12;

    const groundFloor = {
        id: 'floor_0',
        name: 'Parter',
        levelIndex: 0,
        elevationY: 0.0,
        defaultHeight: 2.75,
        isAttic: false,
        blueprint: { dataUrl: null, metersPerPixel: 0.01, widthPx: 1000, heightPx: 800, visible: true },
        walls: [wSW, wSE, wE, wNE, wNW, wW, wPart],
        columns: [],
        furnitures: [
            { id: 'f_fridge', assetId: 'appliance_fridge_60', pos: { x: innerEastX, y: 0, z: -1.20 }, rotY: -Math.PI / 2, wallId: 'w_e', hazard: false },
            { id: 'f_cab1', assetId: 'cabinet_base_60', pos: { x: innerEastX, y: 0, z: -0.60 }, rotY: -Math.PI / 2, wallId: 'w_e', hazard: false },
            { id: 'f_dw', assetId: 'appliance_dishwasher_60', pos: { x: innerEastX, y: 0, z: 0.00 }, rotY: -Math.PI / 2, wallId: 'w_e', hazard: true },
            { id: 'f_sink', assetId: 'cabinet_sink_80', pos: { x: innerEastX, y: 0, z: 0.70 }, rotY: -Math.PI / 2, wallId: 'w_e', hazard: true },
            { id: 'f_wallcab1', assetId: 'cabinet_wall_60', pos: { x: innerEastX, y: 1.45, z: -0.60 }, rotY: -Math.PI / 2, wallId: 'w_e', hazard: false },
            { id: 'f_wallcab2', assetId: 'cabinet_wall_60', pos: { x: innerEastX, y: 1.45, z: 0.00 }, rotY: -Math.PI / 2, wallId: 'w_e', hazard: false },
            { id: 'f_sofa', assetId: 'furniture_sofa_220', pos: { x: innerWestX, y: 0, z: 0.00 }, rotY: Math.PI / 2, wallId: 'w_w', hazard: false },
            { id: 'f_table', assetId: 'furniture_table_160', pos: { x: -1.10, y: 0, z: -0.40 }, rotY: 0, wallId: null, hazard: false }
        ],
        ceilingLights: [
            { id: 'light_kuchnia', category: 'CEILING_LIGHT', worldPos: { x: 1.80, y: 2.73, z: 0.0 }, normal: { x: 0, y: -1, z: 0 } },
            { id: 'light_salon', category: 'CEILING_LIGHT', worldPos: { x: -1.40, y: 2.73, z: 0.0 }, normal: { x: 0, y: -1, z: 0 } }
        ],
        mepLinks: [
            { id: 'lnk_1', switchId: 'mep_sw_main', lightId: 'light_kuchnia' },
            { id: 'lnk_2', switchId: 'mep_sw_main', lightId: 'light_salon' }
        ]
    };

    const firstFloor = {
        id: 'floor_1',
        name: 'Piętro 1',
        levelIndex: 1,
        elevationY: 3.00,
        defaultHeight: 2.75,
        isAttic: false,
        blueprint: { dataUrl: null, metersPerPixel: 0.01, widthPx: 1000, heightPx: 800, visible: true },
        walls: [
            { id: 'aw_s', start: { x: -3.2, z: -2.2 }, end: { x: 3.2, z: -2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [], mep: [] },
            { id: 'aw_e', start: { x: 3.2, z: -2.2 }, end: { x: 3.2, z: 2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [
                { id: 'aop_win1', type: 'WINDOW', dist: 2.2, width: 1.10, height: 1.25, sill: 0.85, flip: false }
            ], mep: [] },
            { id: 'aw_n', start: { x: 3.2, z: 2.2 }, end: { x: -3.2, z: 2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [], mep: [] },
            { id: 'aw_w', start: { x: -3.2, z: 2.2 }, end: { x: -3.2, z: -2.2 }, thickness: 0.24, height: 2.75, clipRoof: false, openings: [
                { id: 'aop_win2', type: 'WINDOW', dist: 2.2, width: 1.10, height: 1.25, sill: 0.85, flip: false }
            ], mep: [] },
            { id: 'aw_part', start: { x: 0.4, z: -2.2 }, end: { x: 0.4, z: 2.2 }, thickness: 0.12, height: 2.75, clipRoof: false, openings: [
                { id: 'aop_door', type: 'DOOR', dist: 2.2, width: 0.85, height: 2.00, sill: 0.0, flip: false }
            ], mep: [] }
        ],
        columns: [],
        furnitures: [],
        ceilingLights: [],
        mepLinks: []
    };

    return {
        schemaVersion: '5.0.0',
        timeOfDayHours: 14.5,
        activeFloorIndex: 0,
        showRoofMesh: false,
        floors: [groundFloor, firstFloor]
    };
}

let projectData = createStarterProject();
const getActiveFloor = () => projectData.floors[projectData.activeFloorIndex];

function executeWithUndo(description, mutatorFn) {
    const beforeJson = JSON.stringify(projectData);
    mutatorFn();
    const afterJson = JSON.stringify(projectData);
    commandHistory.pushSnapshot(description, beforeJson, afterJson, (restoredJson) => {
        projectData = JSON.parse(restoredJson);
        rebuildEntireScene();
    });
    rebuildEntireScene();
}

// ============================================================================
// 2. BUILT-IN FURNITURE & KITCHEN CATALOG DEFINITIONS (Sprint 3)
// ============================================================================
const CATALOG_ITEMS = {
    cabinet_base_60: { name: 'Szafka Dolna 60 cm', size: { x: 0.60, y: 0.85, z: 0.60 }, mountY: 0.0, snapWall: true, hazard: false, hasDoor: true },
    cabinet_sink_80: { name: 'Szafka ze Zlewem 80 cm', size: { x: 0.80, y: 0.85, z: 0.60 }, mountY: 0.0, snapWall: true, hazard: true, hasDoor: true },
    appliance_dishwasher_60: { name: 'Zmywarka 60 cm AGD', size: { x: 0.60, y: 0.85, z: 0.60 }, mountY: 0.0, snapWall: true, hazard: true, hasDoor: true },
    cabinet_wall_60: { name: 'Szafka Wisząca 60 cm', size: { x: 0.60, y: 0.72, z: 0.35 }, mountY: 1.45, snapWall: true, hazard: false, hasDoor: true },
    appliance_fridge_60: { name: 'Lodówka 60×200 cm', size: { x: 0.60, y: 2.00, z: 0.65 }, mountY: 0.0, snapWall: true, hazard: false, hasDoor: true },
    furniture_sofa_220: { name: 'Sofa Salonowa 220 cm', size: { x: 2.20, y: 0.82, z: 0.95 }, mountY: 0.0, snapWall: true, hazard: false, hasDoor: false },
    furniture_table_160: { name: 'Stół Jadalniany 160×90', size: { x: 1.60, y: 0.76, z: 0.90 }, mountY: 0.0, snapWall: false, hazard: false, hasDoor: false },
    custom_glb: { name: 'Własny Model 3D (.GLB)', size: { x: 0.60, y: 0.85, z: 0.60 }, mountY: 0.0, snapWall: true, hazard: false, hasDoor: false }
};

// ============================================================================
// 3. THREE.JS PBR ENGINE, CAMERAS, LIGHTING & MATERIALS
// ============================================================================
const canvas = document.getElementById('viewport-canvas');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.08;

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x181c24);

// Procedural architectural grid
const gridHelper = new THREE.GridHelper(30, 30, 0x3b485e, 0x252c3a);
gridHelper.position.y = -0.002;
scene.add(gridHelper);

// Cameras: Perspective (3D Orbit & FPP Walkthrough) + Orthographic (2D Top-Down & 2D Wall Elevation)
const perspCamera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.06, 300);
const orthoCamera = new THREE.OrthographicCamera(-10, 10, 10, -10, 0.05, 300);
let activeCamera = perspCamera;
let is2DOrthoMode = false;
let isWallElevation2DMode = false;
let isWalkthroughFPP = false;

// Orbit Camera State
const camOrbit = {
    target: new THREE.Vector3(0, 0.8, 0),
    distance: 11.5,
    yaw: -0.65,
    pitch: 0.68,
    orthoScale: 5.5
};

// FPP Walkthrough State
const fppState = {
    pos: new THREE.Vector3(1.1, 0.08, -0.8),
    yaw: 0,
    pitch: 0,
    eyeHeight: 1.68,
    keys: { w: false, a: false, s: false, d: false, shift: false }
};
let isRoomLightingOn = true;

// Real-Time PBR Lighting (Sun + Hemisphere Sky + Interior Point Lights)
const hemiLight = new THREE.HemisphereLight(0xddeeff, 0x3d362d, 0.65);
scene.add(hemiLight);

const sunLight = new THREE.DirectionalLight(0xfff6e5, 2.2);
sunLight.castShadow = true;
sunLight.shadow.mapSize.width = 2048;
sunLight.shadow.mapSize.height = 2048;
sunLight.shadow.camera.near = 0.5;
sunLight.shadow.camera.far = 45;
const dShadow = 10;
sunLight.shadow.camera.left = -dShadow;
sunLight.shadow.camera.right = dShadow;
sunLight.shadow.camera.top = dShadow;
sunLight.shadow.camera.bottom = -dShadow;
sunLight.shadow.bias = -0.0005;
scene.add(sunLight);

// Procedural PBR Textures (Parquet & Wall Plaster)
function createParquetTexture() {
    const c = document.createElement('canvas');
    c.width = 256; c.height = 256;
    const ctx = c.getContext('2d');
    ctx.fillStyle = '#b98b56';
    ctx.fillRect(0, 0, 256, 256);
    ctx.strokeStyle = '#8f6537';
    ctx.lineWidth = 2;
    for (let y = 0; y < 256; y += 32) {
        ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(256, y); ctx.stroke();
        const offset = (y / 32) % 2 === 0 ? 0 : 64;
        for (let x = offset; x < 256; x += 128) {
            ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x, y + 32); ctx.stroke();
        }
    }
    const tex = new THREE.CanvasTexture(c);
    tex.wrapS = THREE.RepeatWrapping;
    tex.wrapT = THREE.RepeatWrapping;
    tex.repeat.set(2, 2);
    return tex;
}

const MAT = {
    wallFront: new THREE.MeshStandardMaterial({ color: 0xf3f2ee, roughness: 0.82, metalness: 0.02, side: THREE.DoubleSide }),
    wallEdge: new THREE.MeshStandardMaterial({ color: 0xc5c4be, roughness: 0.88, metalness: 0.02, side: THREE.DoubleSide }),
    wallSelected: new THREE.MeshStandardMaterial({ color: 0x4dabf7, roughness: 0.5, metalness: 0.1, side: THREE.DoubleSide }),
    floorParquet: new THREE.MeshStandardMaterial({ map: createParquetTexture(), roughness: 0.42, metalness: 0.05, side: THREE.DoubleSide }),
    roofTiles: new THREE.MeshStandardMaterial({ color: 0x2d3138, roughness: 0.65, metalness: 0.15, transparent: true, opacity: 0.88, side: THREE.DoubleSide }),
    frameAnthracite: new THREE.MeshStandardMaterial({ color: 0x26292e, roughness: 0.4, metalness: 0.3 }),
    doorWood: new THREE.MeshStandardMaterial({ color: 0x9c6b3b, roughness: 0.48, metalness: 0.05 }),
    windowGlass: new THREE.MeshPhysicalMaterial({ color: 0xa5d8ff, transparent: true, opacity: 0.34, roughness: 0.08, transmission: 0.6 }),
    cabinetBody: new THREE.MeshStandardMaterial({ color: 0xe9ecef, roughness: 0.6, metalness: 0.05 }),
    cabinetFront: new THREE.MeshStandardMaterial({ color: 0x344050, roughness: 0.32, metalness: 0.12 }),
    countertopOak: new THREE.MeshStandardMaterial({ color: 0xc4945a, roughness: 0.38, metalness: 0.05 }),
    inoxMetal: new THREE.MeshStandardMaterial({ color: 0xb8c0c8, roughness: 0.25, metalness: 0.82 }),
    sofaFabric: new THREE.MeshStandardMaterial({ color: 0x495668, roughness: 0.85, metalness: 0.02 }),
    mepWhite: new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.3, metalness: 0.05 }),
    mepRed400: new THREE.MeshStandardMaterial({ color: 0xe03131, roughness: 0.35, metalness: 0.1 }),
    mepBlueCold: new THREE.MeshStandardMaterial({ color: 0x1c7ed6, roughness: 0.3, metalness: 0.3 }),
    mepRedHot: new THREE.MeshStandardMaterial({ color: 0xf03e3e, roughness: 0.3, metalness: 0.3 }),
    mepSewage: new THREE.MeshStandardMaterial({ color: 0x6c757d, roughness: 0.5, metalness: 0.1 }),
    mepEmissiveOn: new THREE.MeshStandardMaterial({ color: 0xfff9db, emissive: 0xffe066, emissiveIntensity: 2.5 }),
    mepEmissiveOff: new THREE.MeshStandardMaterial({ color: 0x868e96, emissive: 0x000000, emissiveIntensity: 0 }),
    previewValid: new THREE.MeshBasicMaterial({ color: 0x20c997, transparent: true, opacity: 0.58, side: THREE.DoubleSide }),
    previewInvalid: new THREE.MeshBasicMaterial({ color: 0xff4d4f, transparent: true, opacity: 0.68, side: THREE.DoubleSide }),
    concreteColumn: new THREE.MeshStandardMaterial({ color: 0xced4da, roughness: 0.85, metalness: 0.05 }),
    concreteColumnSelected: new THREE.MeshStandardMaterial({ color: 0x4dabf7, roughness: 0.5, metalness: 0.1 }),
    garageAnthracite: new THREE.MeshStandardMaterial({ color: 0x343a40, roughness: 0.42, metalness: 0.22 }),
    garageWhite: new THREE.MeshStandardMaterial({ color: 0xf8f9fa, roughness: 0.45, metalness: 0.05 }),
    garageOak: new THREE.MeshStandardMaterial({ color: 0x936037, roughness: 0.52, metalness: 0.05 }),
    garageBlack: new THREE.MeshStandardMaterial({ color: 0x1a1a1a, roughness: 0.38, metalness: 0.3 }),
    darkTrim: new THREE.MeshStandardMaterial({ color: 0x141517, roughness: 0.5, metalness: 0.1 })
};

const buildingGroup = new THREE.Group();
scene.add(buildingGroup);

const previewGroup = new THREE.Group();
scene.add(previewGroup);

// Live Dimension Lines for MEP & Wall Drawing
const dimLineHoriz = new THREE.Line(
    new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3()]),
    new THREE.LineBasicMaterial({ color: 0x3bc9db, depthTest: false })
);
const dimLineVert = new THREE.Line(
    new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(), new THREE.Vector3()]),
    new THREE.LineBasicMaterial({ color: 0x69db7c, depthTest: false })
);
dimLineHoriz.visible = false;
dimLineVert.visible = false;
scene.add(dimLineHoriz, dimLineVert);

// Interactive objects registry for Raycasting & Walkthrough animations
let wallPickMeshes = [];
let selectableObjects = [];
let wallEndpointHandles = [];
let hoveredWallHandle = null;
let isDraggingWallEndpoint = null;
let walkthroughInteractables = [];
let animatedDoors = [];
let switchLightMap = new Map(); // switchId -> [{ light, bulbMesh, on }]

// ============================================================================
// 4. GEOMETRY ALGORITHMS: MITER JOINS, ATTIC PLANE CLIPPING & WALL MESHES
// ============================================================================
function intersectLines2D(p1, d1, p2, d2) {
    const cross = d1.x * d2.z - d1.z * d2.x;
    if (Math.abs(cross) < 1e-5) return null;
    const dx = p2.x - p1.x;
    const dz = p2.z - p1.z;
    const t = (dx * d2.z - dz * d2.x) / cross;
    return { x: p1.x + d1.x * t, z: p1.z + d1.z * t };
}

function dist2D(a, b) {
    return Math.hypot(b.x - a.x, b.z - a.z);
}

function normalize2D(v) {
    const len = Math.hypot(v.x, v.z);
    return len > 1e-6 ? { x: v.x / len, z: v.z / len } : { x: 1, z: 0 };
}

function setWallMiterCorner(miters, edge, resL, resR) {
    const m = miters.get(edge.wall.id);
    if (!m) return;
    if (edge.isStart) {
        m.startLeft = resL;
        m.startRight = resR;
    } else {
        m.endRight = resL;
        m.endLeft = resR;
    }
}

/**
 * Solves Miter Joins at all shared wall corners (L, T, X junctions).
 * Handles T-junctions by keeping the continuous through-wall solid and planar,
 * while cleanly butting the branch wall against its face (eliminating notches/gaps).
 */
function solveWallMiters(walls) {
    const miters = new Map();
    const clusters = [];
    const snapTol = 0.02;

    for (const w of walls) {
        const dir = normalize2D({ x: w.end.x - w.start.x, z: w.end.z - w.start.z });
        const n = { x: -dir.z, z: dir.x };
        const ht = w.thickness * 0.5;
        miters.set(w.id, {
            startLeft: { x: w.start.x + n.x * ht, z: w.start.z + n.z * ht },
            startRight: { x: w.start.x - n.x * ht, z: w.start.z - n.z * ht },
            endLeft: { x: w.end.x + n.x * ht, z: w.end.z + n.z * ht },
            endRight: { x: w.end.x - n.x * ht, z: w.end.z - n.z * ht },
            dir,
            normal: n
        });

        const addEndpoint = (pt, isStart, outDir) => {
            const leftN = { x: -outDir.z, z: outDir.x };
            const edge = { wall: w, isStart, outDir, leftN, angle: Math.atan2(outDir.z, outDir.x) };
            for (const c of clusters) {
                if (dist2D(c.center, pt) <= snapTol) {
                    c.edges.push(edge);
                    return;
                }
            }
            clusters.push({ center: { ...pt }, edges: [edge] });
        };

        if (dist2D(w.start, w.end) > 0.05) {
            addEndpoint(w.start, true, dir);
            addEndpoint(w.end, false, { x: -dir.x, z: -dir.z });
        }
    }

    for (const cluster of clusters) {
        const { center, edges } = cluster;
        if (edges.length < 2) continue;

        // Check if there is a collinear pair of edges (Continuous Through-Wall at T-junction)
        let collinearA = null;
        let collinearB = null;
        for (let i = 0; i < edges.length; i++) {
            for (let j = i + 1; j < edges.length; j++) {
                const dot = edges[i].outDir.x * edges[j].outDir.x + edges[i].outDir.z * edges[j].outDir.z;
                if (dot < -0.96) {
                    collinearA = edges[i];
                    collinearB = edges[j];
                    break;
                }
            }
            if (collinearA) break;
        }

        if (collinearA && collinearB && edges.length > 2) {
            // T-junction: Through-wall stays continuous and flat without notched cuts
            const nA = collinearA.leftN;
            const htA = collinearA.wall.thickness * 0.5;

            const pL_A = { x: center.x + nA.x * htA, z: center.z + nA.z * htA };
            const pR_A = { x: center.x - nA.x * htA, z: center.z - nA.z * htA };

            setWallMiterCorner(miters, collinearA, pL_A, pR_A);
            setWallMiterCorner(miters, collinearB, pR_A, pL_A);

            // Abutting branch walls terminate against the near face of the through-wall
            for (const edge of edges) {
                if (edge === collinearA || edge === collinearB) continue;

                const htBranch = edge.wall.thickness * 0.5;
                const dotWithNormal = edge.outDir.x * nA.x + edge.outDir.z * nA.z;
                const sign = dotWithNormal >= 0 ? 1 : -1;

                const cosTheta = Math.max(0.2, Math.abs(dotWithNormal));
                const distToFace = htA / cosTheta;

                const buttCenter = {
                    x: center.x + edge.outDir.x * distToFace,
                    z: center.z + edge.outDir.z * distToFace
                };

                const bL = { x: buttCenter.x + edge.leftN.x * htBranch, z: buttCenter.z + edge.leftN.z * htBranch };
                const bR = { x: buttCenter.x - edge.leftN.x * htBranch, z: buttCenter.z - edge.leftN.z * htBranch };

                setWallMiterCorner(miters, edge, bL, bR);
            }
            continue;
        }

        // Standard radial miter sorting for L-corners and clean bends
        edges.sort((a, b) => a.angle - b.angle);

        for (let i = 0; i < edges.length; i++) {
            const curr = edges[i];
            const ccw = edges[(i + 1) % edges.length];
            const cw = edges[(i - 1 + edges.length) % edges.length];

            const ht = curr.wall.thickness * 0.5;
            const ccwHt = ccw.wall.thickness * 0.5;
            const cwHt = cw.wall.thickness * 0.5;
            const limit = Math.max(curr.wall.thickness, ccw.wall.thickness, cw.wall.thickness) * 2.5;

            const pLeft = { x: center.x + curr.leftN.x * ht, z: center.z + curr.leftN.z * ht };
            const ccwRight = { x: center.x - ccw.leftN.x * ccwHt, z: center.z - ccw.leftN.z * ccwHt };
            const ixL = intersectLines2D(pLeft, curr.outDir, ccwRight, ccw.outDir);
            const resL = (ixL && dist2D(center, ixL) <= limit) ? ixL : pLeft;

            const pRight = { x: center.x - curr.leftN.x * ht, z: center.z - curr.leftN.z * ht };
            const cwLeft = { x: center.x + cw.leftN.x * cwHt, z: center.z + cw.leftN.z * cwHt };
            const ixR = intersectLines2D(pRight, curr.outDir, cwLeft, cw.outDir);
            const resR = (ixR && dist2D(center, ixR) <= limit) ? ixR : pRight;

            setWallMiterCorner(miters, curr, resL, resR);
        }
    }
    return miters;
}

/**
 * Sutherland-Hodgman 3D polygon clipping against Attic Roof Planes.
 * Roof plane equation: planeDist(pt) >= 0 means inside the usable attic volume.
 */
function clipPolygonByRoofPlanes(poly3D, roofPlanes) {
    if (!roofPlanes || roofPlanes.length === 0) return poly3D;
    let current = poly3D;
    for (const plane of roofPlanes) {
        if (current.length < 3) break;
        const next = [];
        for (let i = 0; i < current.length; i++) {
            const a = current[i];
            const b = current[(i + 1) % current.length];
            const da = plane.nx * (a.x - plane.ox) + plane.ny * (a.y - plane.oy) + plane.nz * (a.z - plane.oz);
            const db = plane.nx * (b.x - plane.ox) + plane.ny * (b.y - plane.oy) + plane.nz * (b.z - plane.oz);
            const aIn = da >= -1e-4;
            const bIn = db >= -1e-4;

            if (aIn && bIn) {
                next.push(b);
            } else if (aIn && !bIn) {
                const t = da / (da - db);
                next.push({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, z: a.z + (b.z - a.z) * t });
            } else if (!aIn && bIn) {
                const t = da / (da - db);
                next.push({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, z: a.z + (b.z - a.z) * t });
                next.push(b);
            }
        }
        current = next;
    }
    return current;
}

function buildAtticRoofPlanes(floor) {
    return [];
}

function buildProceduralWallGeometry(wall, miter, roofPlanes) {
    const length = dist2D(wall.start, wall.end);
    if (length < 0.05) return new THREE.BufferGeometry();

    const pos = [];
    const uvs = [];
    const indices = [];

    const appendPoly = (pts3D, invert = false) => {
        if (pts3D.length < 3) return;
        const base = pos.length / 3;
        for (const p of pts3D) {
            pos.push(p.x, p.y, p.z);
            uvs.push(p.x + p.z, p.y);
        }
        for (let i = 1; i < pts3D.length - 1; i++) {
            if (!invert) {
                indices.push(base, base + i + 1, base + i);
            } else {
                indices.push(base, base + i, base + i + 1);
            }
        }
    };

    const lerp2D = (p0, p1, t) => ({ x: p0.x + (p1.x - p0.x) * t, z: p0.z + (p1.z - p0.z) * t });

    const effectiveWallH = (wall.clipRoof && roofPlanes && roofPlanes.length > 0 && roofPlanes[0].maxRidgeY)
        ? Math.max(wall.height, roofPlanes[0].maxRidgeY)
        : wall.height;

    const addSegment = (u0, u1, y0, y1) => {
        const t0 = Math.max(0, Math.min(1, u0 / length));
        const t1 = Math.max(0, Math.min(1, u1 / length));
        const f0 = lerp2D(miter.startLeft, miter.endLeft, t0);
        const f1 = lerp2D(miter.startLeft, miter.endLeft, t1);
        const b0 = lerp2D(miter.startRight, miter.endRight, t0);
        const b1 = lerp2D(miter.startRight, miter.endRight, t1);

        const activePlanes = wall.clipRoof ? roofPlanes : [];

        const frontPoly = clipPolygonByRoofPlanes([
            { x: f0.x, y: y0, z: f0.z }, { x: f1.x, y: y0, z: f1.z },
            { x: f1.x, y: y1, z: f1.z }, { x: f0.x, y: y1, z: f0.z }
        ], activePlanes);
        appendPoly(frontPoly, false);

        const backPoly = clipPolygonByRoofPlanes([
            { x: b0.x, y: y0, z: b0.z }, { x: b1.x, y: y0, z: b1.z },
            { x: b1.x, y: y1, z: b1.z }, { x: b0.x, y: y1, z: b0.z }
        ], activePlanes);
        appendPoly(backPoly, true);

        const leftCap = clipPolygonByRoofPlanes([
            { x: b0.x, y: y0, z: b0.z }, { x: b0.x, y: y1, z: b0.z },
            { x: f0.x, y: y1, z: f0.z }, { x: f0.x, y: y0, z: f0.z }
        ], activePlanes);
        appendPoly(leftCap, false);

        const rightCap = clipPolygonByRoofPlanes([
            { x: f1.x, y: y0, z: f1.z }, { x: f1.x, y: y1, z: f1.z },
            { x: b1.x, y: y1, z: b1.z }, { x: b1.x, y: y0, z: b1.z }
        ], activePlanes);
        appendPoly(rightCap, false);

        const topCap = clipPolygonByRoofPlanes([
            { x: f0.x, y: y1, z: f0.z }, { x: f1.x, y: y1, z: f1.z },
            { x: b1.x, y: y1, z: b1.z }, { x: b0.x, y: y1, z: b0.z }
        ], activePlanes);
        appendPoly(topCap, false);

        // Seal sloped top edges along roof clipping planes
        if (activePlanes.length > 0) {
            const boxPlanes = [
                { ox: 0, oy: y0, oz: 0, nx: 0, ny: 1, nz: 0 },
                { ox: 0, oy: y1, oz: 0, nx: 0, ny: -1, nz: 0 }
            ];
            for (let pIdx = 0; pIdx < activePlanes.length; pIdx++) {
                const pl = activePlanes[pIdx];
                if (Math.abs(pl.ny) < 1e-4) continue;
                const evalY = (pt) => pl.oy + (pl.nx * (pt.x - pl.ox) + pl.nz * (pt.z - pl.oz)) / (-pl.ny);
                const rawSlopeCap = [
                    { x: f0.x, y: evalY(f0), z: f0.z },
                    { x: f1.x, y: evalY(f1), z: f1.z },
                    { x: b1.x, y: evalY(b1), z: b1.z },
                    { x: b0.x, y: evalY(b0), z: b0.z }
                ];
                const otherPlanes = activePlanes.filter((_, idx) => idx !== pIdx).concat(boxPlanes);
                const clippedSlopeCap = clipPolygonByRoofPlanes(rawSlopeCap, otherPlanes);
                appendPoly(clippedSlopeCap, false);
            }
        }

        const botCap = clipPolygonByRoofPlanes([
            { x: f0.x, y: y0, z: f0.z }, { x: b0.x, y: y0, z: b0.z },
            { x: b1.x, y: y0, z: b1.z }, { x: f1.x, y: y0, z: f1.z }
        ], activePlanes);
        appendPoly(botCap, false);
    };

    const sortedOps = [...(wall.openings || [])]
        .filter(o => !(activeTool === 'MOVE_OBJECT' && movingObject?.type === 'OPENING' && movingObject.id === o.id))
        .sort((a, b) => a.dist - b.dist);
    let cursor = 0;
    for (const op of sortedOps) {
        const s = Math.max(0, op.dist - op.width * 0.5);
        const e = Math.min(length, op.dist + op.width * 0.5);
        if (s > cursor + 0.01) addSegment(cursor, s, 0, effectiveWallH);
        if (op.sill > 0.01) addSegment(s, e, 0, op.sill);
        if (op.sill + op.height < effectiveWallH - 0.01) addSegment(s, e, op.sill + op.height, effectiveWallH);
        cursor = e;
    }
    if (cursor < length - 0.01) addSegment(cursor, length, 0, effectiveWallH);

    const geom = new THREE.BufferGeometry();
    geom.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    geom.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
    geom.setIndex(indices);
    geom.computeVertexNormals();
    return geom;
}

function buildWallEndpointHandles(wall, floorGroup) {
    wallEndpointHandles = [];
    if (!wall || isWalkthroughFPP) return;

    const endpoints = [
        { name: 'start', pt: wall.start, label: 'Początek' },
        { name: 'end', pt: wall.end, label: 'Koniec' }
    ];

    const wallH = wall.height || 2.75;

    for (const ep of endpoints) {
        const handleGroup = new THREE.Group();
        handleGroup.position.set(ep.pt.x, 0, ep.pt.z);

        // 1. Vertical axis glowing cylinder
        const pinGeom = new THREE.CylinderGeometry(0.065, 0.065, wallH, 16);
        pinGeom.translate(0, wallH * 0.5, 0);
        const pinMat = new THREE.MeshStandardMaterial({
            color: 0x1971c2,
            roughness: 0.25,
            metalness: 0.2,
            emissive: 0x0c3b6d,
            emissiveIntensity: 0.5
        });
        const pinMesh = new THREE.Mesh(pinGeom, pinMat);
        handleGroup.add(pinMesh);

        // 2. Top grip sphere
        const sphereGeom = new THREE.SphereGeometry(0.18, 16, 16);
        sphereGeom.translate(0, wallH + 0.12, 0);
        const sphereMat = new THREE.MeshStandardMaterial({
            color: 0x1971c2,
            roughness: 0.2,
            metalness: 0.3,
            emissive: 0x0c3b6d,
            emissiveIntensity: 0.7
        });
        const sphereMesh = new THREE.Mesh(sphereGeom, sphereMat);
        handleGroup.add(sphereMesh);

        // 3. Ground / 2D ring (clearly visible from top-down ortho 2D view)
        const ringGeom = new THREE.RingGeometry(0.16, 0.28, 24);
        ringGeom.rotateX(-Math.PI / 2);
        ringGeom.translate(0, 0.02, 0);
        const ringMat = new THREE.MeshBasicMaterial({
            color: 0x339af0,
            transparent: true,
            opacity: 0.9,
            side: THREE.DoubleSide
        });
        const ringMesh = new THREE.Mesh(ringGeom, ringMat);
        handleGroup.add(ringMesh);

        // 4. Large Invisible Hitbox for easy clicking (radius 0.38m, full wall height + padding)
        const hitGeom = new THREE.CylinderGeometry(0.38, 0.38, wallH + 0.40, 12);
        hitGeom.translate(0, (wallH + 0.40) * 0.5, 0);
        const hitMesh = new THREE.Mesh(hitGeom, new THREE.MeshBasicMaterial({ visible: false }));
        handleGroup.add(hitMesh);

        handleGroup.userData = {
            kind: 'WALL_HANDLE',
            wall,
            endpoint: ep.name,
            pinMesh,
            sphereMesh,
            ringMesh
        };

        floorGroup.add(handleGroup);
        wallEndpointHandles.push(handleGroup);
        selectableObjects.push(handleGroup);
    }
}

function startDraggingWallEndpoint(wall, endpoint, clientX, clientY) {
    closeAllDropdownMenus();
    const fixedEndpoint = endpoint === 'start' ? 'end' : 'start';
    const fixedPt = { ...wall[fixedEndpoint] };
    const origPt = { ...wall[endpoint] };
    const origLen = dist2D(wall.start, wall.end);

    isDraggingWallEndpoint = {
        wall,
        endpoint,
        fixedEndpoint,
        fixedPt,
        origPt,
        origLen,
        initialStart: { ...wall.start },
        initialEnd: { ...wall.end }
    };

    canvas.style.cursor = 'grabbing';
    showFloatingBadge(clientX, clientY, `Długość: ${origLen.toFixed(2)} m`);
    setTelemetry(`Przeciąganie końca ściany (${endpoint === 'start' ? 'początek' : 'koniec'}). Przesuwaj kursor myszy, aby skrócić lub wydłużyć ścianę (Shift = kąt 45°).`);
}

function updateDraggingWallEndpoint(clientX, clientY, shiftHeld) {
    if (!isDraggingWallEndpoint) return;
    const { wall, endpoint, fixedPt, origPt, origLen, initialStart, initialEnd } = isDraggingWallEndpoint;
    const pt = getFloorIntersectionXZ(clientX, clientY);
    if (!pt) return;

    // 1. Determine direction constraint
    // By default, preserve the wall's line / axis (strictly orthogonal or original line)
    const isHoriz = Math.abs(initialStart.z - initialEnd.z) < 0.04;
    const isVert = Math.abs(initialStart.x - initialEnd.x) < 0.04;

    let candidate = { x: pt.x, z: pt.z };

    if (!shiftHeld) {
        if (isHoriz) {
            candidate.z = fixedPt.z;
        } else if (isVert) {
            candidate.x = fixedPt.x;
        } else {
            // Project along original wall line
            const dir = normalize2D({ x: origPt.x - fixedPt.x, z: origPt.z - fixedPt.z });
            const proj = (pt.x - fixedPt.x) * dir.x + (pt.z - fixedPt.z) * dir.z;
            candidate = {
                x: fixedPt.x + dir.x * Math.max(0.20, proj),
                z: fixedPt.z + dir.z * Math.max(0.20, proj)
            };
        }
    } else {
        // Shift held: 45° angle snap from fixedPt
        const dx = pt.x - fixedPt.x;
        const dz = pt.z - fixedPt.z;
        const angle = Math.atan2(dz, dx);
        const snappedAngle = Math.round(angle / (Math.PI / 4)) * (Math.PI / 4);
        const dist = Math.hypot(dx, dz);
        candidate = {
            x: fixedPt.x + Math.cos(snappedAngle) * dist,
            z: fixedPt.z + Math.sin(snappedAngle) * dist
        };
    }

    // 2. Node snapping to other walls
    const floor = getActiveFloor();
    let snappedNode = false;
    for (const otherW of floor.walls) {
        if (otherW.id === wall.id) continue;
        if (dist2D(candidate, otherW.start) < 0.25) {
            candidate = { ...otherW.start };
            snappedNode = true;
            break;
        }
        if (dist2D(candidate, otherW.end) < 0.25) {
            candidate = { ...otherW.end };
            snappedNode = true;
            break;
        }
    }

    // 3. Grid / Step snapping (0.05 m = 5 cm steps if not node-snapped)
    let newLen = dist2D(fixedPt, candidate);
    if (!snappedNode) {
        newLen = Math.max(0.20, Math.round(newLen * 20) / 20);
        const curDir = normalize2D({ x: candidate.x - fixedPt.x, z: candidate.z - fixedPt.z });
        candidate = {
            x: Math.round((fixedPt.x + curDir.x * newLen) * 1000) / 1000,
            z: Math.round((fixedPt.z + curDir.z * newLen) * 1000) / 1000
        };
    }

    // Update wall coordinates
    wall[endpoint].x = candidate.x;
    wall[endpoint].z = candidate.z;

    // Update input in options bar
    const wL = document.getElementById('inp-wall-length');
    if (wL) wL.value = newLen.toFixed(2);

    // Delta compared to original
    const delta = newLen - origLen;
    const deltaStr = Math.abs(delta) > 0.01 ? ` (${delta > 0 ? '+' : ''}${delta.toFixed(2)} m)` : '';
    showFloatingBadge(clientX, clientY, `Długość: ${newLen.toFixed(2)} m${deltaStr}`);
    setTelemetry(`Modyfikacja długości ściany: ${newLen.toFixed(2)} m${deltaStr}${snappedNode ? ' [PRZYCIĄGNIĘTO DO NAROŻNIKA]' : ''}`);

    // Rebuild visual scene live
    rebuildEntireScene();
}

function finishDraggingWallEndpoint() {
    if (!isDraggingWallEndpoint) return;
    const { wall, endpoint, initialStart, initialEnd } = isDraggingWallEndpoint;
    const finalStart = { ...wall.start };
    const finalEnd = { ...wall.end };
    isDraggingWallEndpoint = null;
    canvas.style.cursor = 'default';
    hideFloatingBadge();

    const changed = (initialStart.x !== finalStart.x || initialStart.z !== finalStart.z ||
                     initialEnd.x !== finalEnd.x || initialEnd.z !== finalEnd.z);

    if (changed) {
        wall.start = { ...initialStart };
        wall.end = { ...initialEnd };

        executeWithUndo('Zmień długość ściany', () => {
            wall.start = { ...finalStart };
            wall.end = { ...finalEnd };
            const newLen = dist2D(wall.start, wall.end);
            (wall.openings || []).forEach(op => {
                if (op.dist + op.width > newLen) {
                    op.dist = Math.max(0.1, newLen - op.width - 0.1);
                }
            });
        });
        rebuildEntireScene();
        const finalLen = dist2D(finalStart, finalEnd);
        setTelemetry(`✅ Zmieniono długość ściany: ${finalLen.toFixed(2)} m.`);
    }
}

function cancelDraggingWallEndpoint() {
    if (!isDraggingWallEndpoint) return;
    const { wall, initialStart, initialEnd } = isDraggingWallEndpoint;
    wall.start = { ...initialStart };
    wall.end = { ...initialEnd };
    isDraggingWallEndpoint = null;
    canvas.style.cursor = 'default';
    hideFloatingBadge();
    rebuildEntireScene();
    setTelemetry('Anulowano zmianę długości ściany.');
}

// ============================================================================
// 5. SCENE REBUILDER (Walls, Openings, Roof, Furniture, MEP & Wiring)
// ============================================================================
let showAllFloorsInCad = false;

function rebuildEntireScene() {
    while (buildingGroup.children.length > 0) {
        buildingGroup.remove(buildingGroup.children[0]);
    }
    wallPickMeshes = [];
    selectableObjects = [];
    wallEndpointHandles = [];
    walkthroughInteractables = [];
    animatedDoors = [];
    switchLightMap.clear();

    const activeIdx = projectData.activeFloorIndex;

    projectData.floors.forEach((floor, fIdx) => {
        // Hide upper floor in CAD Design mode when editing Ground Floor (0) unless showAllFloorsInCad is enabled, and show all in Walkthrough FPP
        if (!isWalkthroughFPP && !showAllFloorsInCad && fIdx > activeIdx) return;

        const floorGroup = new THREE.Group();
        floorGroup.position.y = floor.elevationY;
        buildingGroup.add(floorGroup);

        // 1. Blueprint Reference Quad
        if (floor.blueprint && floor.blueprint.dataUrl && !isWalkthroughFPP) {
            const tex = new THREE.TextureLoader().load(floor.blueprint.dataUrl);
            const wMeters = (floor.blueprint.widthPx || 1000) * floor.blueprint.metersPerPixel;
            const hMeters = (floor.blueprint.heightPx || 800) * floor.blueprint.metersPerPixel;
            const bpMesh = new THREE.Mesh(
                new THREE.PlaneGeometry(wMeters, hMeters),
                new THREE.MeshBasicMaterial({ map: tex, transparent: true, opacity: 0.65, side: THREE.DoubleSide })
            );
            bpMesh.rotation.x = -Math.PI / 2;
            bpMesh.position.x = floor.blueprint.offsetX || 0;
            bpMesh.position.y = 0.005;
            bpMesh.position.z = floor.blueprint.offsetZ || 0;
            floorGroup.add(bpMesh);
        }

        // 2. Solve Miter Joins & Attic Roof Planes
        const miters = solveWallMiters(floor.walls);
        const roofPlanes = buildAtticRoofPlanes(floor);

        // 3. Build Walls + Window/Door Frames
        for (const wall of floor.walls) {
            if (activeTool === 'MOVE_OBJECT' && movingObject?.type === 'WALL' && movingObject.id === wall.id) continue;
            const miter = miters.get(wall.id);
            if (!miter) continue;

            const geom = buildProceduralWallGeometry(wall, miter, roofPlanes);
            const isSelected = selectedItem && selectedItem.type === 'WALL' && selectedItem.id === wall.id;
            const wallMesh = new THREE.Mesh(geom, isSelected ? MAT.wallSelected : MAT.wallFront);
            wallMesh.castShadow = true;
            wallMesh.receiveShadow = true;
            wallMesh.userData = { kind: 'WALL', floorIdx: fIdx, wall, miter };
            floorGroup.add(wallMesh);

            if (fIdx === activeIdx) {
                wallPickMeshes.push(wallMesh);
                selectableObjects.push(wallMesh);
            }

            // Build 3D Window & Door Frames
            for (const op of (wall.openings || [])) {
                if (activeTool === 'MOVE_OBJECT' && movingObject?.type === 'OPENING' && movingObject.id === op.id) continue;
                const opGroup = createOpening3DAssembly(wall, op, miter);
                opGroup.userData = { kind: 'OPENING', floorIdx: fIdx, wall, opening: op };
                floorGroup.add(opGroup);
                if (fIdx === activeIdx) selectableObjects.push(opGroup);
            }

            // Build Wall-mounted MEP Fixtures
            for (const mep of (wall.mep || [])) {
                if (activeTool === 'MOVE_OBJECT' && movingObject?.type === 'MEP' && movingObject.id === mep.id) continue;
                const mepGroup = createMEP3DFixture(mep);
                mepGroup.userData = { kind: 'MEP', floorIdx: fIdx, wall, mep };
                floorGroup.add(mepGroup);
                if (fIdx === activeIdx) selectableObjects.push(mepGroup);
            }
        }

        // 3b. Build Interactive Wall Endpoint Handles if a wall is selected on this floor
        if (fIdx === activeIdx && selectedItem && selectedItem.type === 'WALL' && !isWalkthroughFPP) {
            const selWall = floor.walls.find(w => w.id === selectedItem.id);
            if (selWall) {
                const wL = document.getElementById('inp-wall-length');
                if (wL && !isDraggingWallEndpoint) wL.value = dist2D(selWall.start, selWall.end).toFixed(2);
                buildWallEndpointHandles(selWall, floorGroup);
            }
        }

        // 3c. Build Structural Columns / Pillars
        for (const col of (floor.columns || [])) {
            if (activeTool === 'MOVE_OBJECT' && movingObject?.type === 'COLUMN' && movingObject.id === col.id) continue;
            const colGroup = createColumn3DInstance(col, floor.defaultHeight);
            colGroup.position.set(col.pos.x, 0, col.pos.z);
            colGroup.userData = { kind: 'COLUMN', floorIdx: fIdx, column: col };
            floorGroup.add(colGroup);
            if (fIdx === activeIdx) selectableObjects.push(colGroup);
        }

        // 4. Build Floor Slab from Wall Bounding Footprint
        buildFloorSlabAndStats(floor, floorGroup, fIdx === activeIdx);

        // 5. Build 3D Gable Roof Slabs on Attic
        if (floor.isAttic && projectData.showRoofMesh && floor.walls.length > 0) {
            buildAtticRoofSlabs3D(floor, floorGroup);
        }

        // 6. Build Kitchen Cabinets, Appliances & Custom GLTF Furniture
        for (const furn of (floor.furnitures || [])) {
            if (activeTool === 'MOVE_OBJECT' && movingObject?.type === 'FURNITURE' && movingObject.id === furn.id) continue;
            const furnGroup = createFurniture3DInstance(furn);
            furnGroup.position.set(furn.pos.x, furn.pos.y, furn.pos.z);
            furnGroup.rotation.y = furn.rotY || 0;
            furnGroup.userData = { kind: 'FURNITURE', floorIdx: fIdx, furn };
            floorGroup.add(furnGroup);
            if (fIdx === activeIdx) selectableObjects.push(furnGroup);
        }

        // 7. Build Ceiling Light Points & Switch -> Light Wiring Links
        const lightLookup = new Map();
        for (const cl of (floor.ceilingLights || [])) {
            if (activeTool === 'MOVE_OBJECT' && movingObject?.type === 'CEILING_LIGHT' && movingObject.id === cl.id) continue;
            const lightObj = createMEP3DFixture(cl);
            lightObj.userData = { kind: 'CEILING_LIGHT', floorIdx: fIdx, mep: cl };
            floorGroup.add(lightObj);
            if (fIdx === activeIdx) selectableObjects.push(lightObj);
            if (lightObj.userData.lightRef) {
                cl._lightRef = lightObj.userData.lightRef;
                lightLookup.set(cl.id, lightObj.userData.lightRef);
            }
        }

        for (const lnk of (floor.mepLinks || [])) {
            const targetLightInfo = lightLookup.get(lnk.lightId);
            if (targetLightInfo) {
                if (!switchLightMap.has(lnk.switchId)) switchLightMap.set(lnk.switchId, []);
                switchLightMap.get(lnk.switchId).push(targetLightInfo);
            }

            // Draw yellow electrical circuit line in CAD mode
            if (!isWalkthroughFPP) {
                let swPos = null;
                for (const w of floor.walls) {
                    const foundSw = (w.mep || []).find(m => m.id === lnk.switchId);
                    if (foundSw) { swPos = foundSw.worldPos; break; }
                }
                const foundLt = (floor.ceilingLights || []).find(l => l.id === lnk.lightId);
                if (swPos && foundLt) {
                    const pts = [
                        new THREE.Vector3(swPos.x, swPos.y, swPos.z),
                        new THREE.Vector3(swPos.x, foundLt.worldPos.y - 0.02, swPos.z),
                        new THREE.Vector3(foundLt.worldPos.x, foundLt.worldPos.y - 0.02, foundLt.worldPos.z)
                    ];
                    const wire = new THREE.Line(
                        new THREE.BufferGeometry().setFromPoints(pts),
                        new THREE.LineBasicMaterial({ color: 0xffd43b })
                    );
                    floorGroup.add(wire);
                }
            }
        }
    });
}

function createColumn3DInstance(col, defaultHeight) {
    const group = new THREE.Group();
    const h = col.height || defaultHeight || 2.75;
    const w = col.width || 0.30;
    const d = col.depth || col.width || 0.30;
    const isSelected = selectedItem && selectedItem.type === 'COLUMN' && selectedItem.id === col.id;
    const mat = isSelected ? MAT.wallSelected : MAT.concreteColumn;

    let geom;
    if (col.round) {
        geom = new THREE.CylinderGeometry(w * 0.5, w * 0.5, h, 28);
    } else {
        geom = new THREE.BoxGeometry(w, h, d);
    }
    const mesh = new THREE.Mesh(geom, mat);
    mesh.position.y = h * 0.5;
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    group.add(mesh);

    // Architectural edge outlines (visible in both 2D and 3D)
    if (!col.round) {
        const edges = new THREE.LineSegments(
            new THREE.EdgesGeometry(geom),
            new THREE.LineBasicMaterial({ color: isSelected ? 0x1971c2 : 0x495057, linewidth: 1.5 })
        );
        edges.position.y = h * 0.5;
        group.add(edges);

        // Top structural 'X' diagonals for standard architectural plan representation
        const hw = w * 0.5, hd = d * 0.5;
        const xPts = [
            new THREE.Vector3(-hw, h + 0.005, -hd),
            new THREE.Vector3(hw, h + 0.005, hd),
            new THREE.Vector3(-hw, h + 0.005, hd),
            new THREE.Vector3(hw, h + 0.005, -hd)
        ];
        const xLines = new THREE.LineSegments(
            new THREE.BufferGeometry().setFromPoints(xPts),
            new THREE.LineBasicMaterial({ color: isSelected ? 0x1971c2 : 0x343a40 })
        );
        group.add(xLines);
    } else {
        const rad = w * 0.5;
        const xPts = [
            new THREE.Vector3(-rad, h + 0.005, 0),
            new THREE.Vector3(rad, h + 0.005, 0),
            new THREE.Vector3(0, h + 0.005, -rad),
            new THREE.Vector3(0, h + 0.005, rad)
        ];
        const xLines = new THREE.LineSegments(
            new THREE.BufferGeometry().setFromPoints(xPts),
            new THREE.LineBasicMaterial({ color: isSelected ? 0x1971c2 : 0x343a40 })
        );
        group.add(xLines);
    }

    group.userData = { kind: 'COLUMN', column: col };
    return group;
}

function createOpening3DAssembly(wall, op, miter) {
    const group = new THREE.Group();
    const cx = wall.start.x + miter.dir.x * op.dist;
    const cz = wall.start.z + miter.dir.z * op.dist;
    group.position.set(cx, op.sill, cz);
    group.rotation.y = Math.atan2(miter.normal.x, miter.normal.z);

    const fThick = 0.05;
    const depth = wall.thickness + 0.02;

    const addBox = (w, h, d, x, y, z, mat, parent = group) => {
        const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
        m.position.set(x, y, z);
        m.castShadow = true;
        parent.add(m);
        return m;
    };

    // Frame jambs
    addBox(fThick, op.height, depth, -op.width * 0.5 + fThick * 0.5, op.height * 0.5, 0, MAT.frameAnthracite);
    addBox(fThick, op.height, depth, op.width * 0.5 - fThick * 0.5, op.height * 0.5, 0, MAT.frameAnthracite);
    addBox(op.width, fThick, depth, 0, op.height - fThick * 0.5, 0, MAT.frameAnthracite);

    if (op.type === 'WINDOW') {
        addBox(op.width, fThick, depth, 0, fThick * 0.5, 0, MAT.frameAnthracite);
        addBox(op.width + 0.06, 0.03, depth + 0.06, 0, -0.015, 0, MAT.frameAnthracite);
        if (op.width >= 0.95) addBox(fThick * 0.8, op.height - fThick * 2, fThick, 0, op.height * 0.5, 0, MAT.frameAnthracite);
        addBox(op.width - fThick * 2, op.height - fThick * 2, 0.016, 0, op.height * 0.5, 0, MAT.windowGlass);
    } else if (op.type === 'GARAGE_DOOR') {
        // Sectional Overhead Garage Door (Brama garażowa segmentowa)
        const innerW = op.width - fThick * 2;
        const innerH = op.height - fThick;

        let panelMat = MAT.garageAnthracite;
        if (op.color === 'white') panelMat = MAT.garageWhite;
        else if (op.color === 'wood' || op.color === 'oak') panelMat = MAT.garageOak;
        else if (op.color === 'black') panelMat = MAT.garageBlack;

        // Top lintel / header trim
        addBox(innerW, 0.04, depth + 0.02, 0, op.height - fThick - 0.02, 0, MAT.frameAnthracite);

        // Sectional panels (4 horizontal panels with groove reveals)
        const numPanels = 4;
        const panelH = (innerH - 0.02) / numPanels;
        for (let p = 0; p < numPanels; p++) {
            const py = panelH * 0.5 + p * panelH;
            // Panel body
            addBox(innerW - 0.02, panelH - 0.016, 0.046, 0, py, 0, panelMat);
            // Horizontal shadow joint reveal between panels
            if (p > 0) {
                addBox(innerW - 0.02, 0.012, 0.050, 0, p * panelH, 0, MAT.darkTrim);
            }
            // Horizontal embossed architectural ribbing
            addBox(innerW - 0.08, 0.008, 0.048, 0, py, 0, MAT.darkTrim);
        }

        // Bottom rubber sealing strip
        addBox(innerW, 0.035, 0.052, 0, 0.018, 0, MAT.darkTrim);

        // Inox handle / latch lock
        addBox(0.24, 0.04, 0.065, 0, panelH * 0.9, 0, MAT.inoxMetal);

        // Overhead ceiling guide tracks (prowadnice sufitowe) extending into room interior
        const trackLen = 2.40;
        const trackZ = trackLen * 0.5;
        addBox(0.04, 0.04, trackLen, -(innerW * 0.5 - 0.06), op.height - 0.06, trackZ, MAT.inoxMetal);
        addBox(0.04, 0.04, trackLen, (innerW * 0.5 - 0.06), op.height - 0.06, trackZ, MAT.inoxMetal);
        addBox(0.03, op.height, 0.03, -(innerW * 0.5 - 0.06), op.height * 0.5, 0.05, MAT.inoxMetal);
        addBox(0.03, op.height, 0.03, (innerW * 0.5 - 0.06), op.height * 0.5, 0.05, MAT.inoxMetal);
    } else {
        // Hinged Door Leaf (interactive in Walkthrough FPP!)
        const innerW = op.width - fThick * 2;
        const innerH = op.height - fThick;
        const sign = op.flip ? 1 : -1;
        const hinge = new THREE.Group();
        hinge.position.set(sign * innerW * 0.5, 0, 0);
        group.add(hinge);

        const leaf = addBox(innerW, innerH, 0.042, -sign * innerW * 0.5, innerH * 0.5, 0, MAT.doorWood, hinge);
        addBox(0.12, 0.025, 0.13, -sign * innerW * 0.82, innerH * 0.5, 0, MAT.inoxMetal, hinge);

        const animState = { hinge, isOpen: false, targetAngle: 0, maxAngle: -sign * (Math.PI * 0.48) };
        animatedDoors.push(animState);
        leaf.userData.interactable = {
            getPrompt: () => animState.isOpen ? '[LPM / E] Zamknij drzwi' : '[LPM / E] Otwórz drzwi',
            interact: () => {
                animState.isOpen = !animState.isOpen;
                animState.targetAngle = animState.isOpen ? animState.maxAngle : 0;
            }
        };
        walkthroughInteractables.push(leaf);
    }
    return group;
}

function createFurniture3DInstance(furn) {
    const group = new THREE.Group();
    if (furn.assetId.startsWith('custom_glb_') && customGltfCache.has(furn.assetId)) {
        const clone = customGltfCache.get(furn.assetId).clone(true);
        group.add(clone);
        return group;
    }

    const cat = CATALOG_ITEMS[furn.assetId] || CATALOG_ITEMS.cabinet_base_60;
    const size = furn.customSize || cat.size;
    const { x: w, y: h, z: d } = size;

    const addPart = (bw, bh, bd, px, py, pz, mat, parent = group) => {
        const m = new THREE.Mesh(new THREE.BoxGeometry(bw, bh, bd), mat);
        m.position.set(px, py, pz);
        m.castShadow = true;
        m.receiveShadow = true;
        parent.add(m);
        return m;
    };

    if (furn.assetId.startsWith('custom_furn_')) {
        let mat = MAT.countertopOak;
        if (cat.customMat === 'white') mat = MAT.cabinetFront;
        else if (cat.customMat === 'anthracite') mat = MAT.frameAnthracite;
        else if (cat.customMat === 'steel') mat = MAT.inoxMetal;

        addPart(w, h, d, 0, h * 0.5, d * 0.5, mat);
        addPart(w + 0.01, 0.02, d + 0.01, 0, h - 0.01, d * 0.5, MAT.countertopOak);
        return group;
    }

    if (furn.assetId === 'furniture_table_160') {
        addPart(w, 0.04, d, 0, h - 0.02, d * 0.5, MAT.countertopOak);
        const legH = h - 0.04;
        addPart(0.06, legH, 0.06, -w * 0.45, legH * 0.5, 0.08, MAT.frameAnthracite);
        addPart(0.06, legH, 0.06, w * 0.45, legH * 0.5, 0.08, MAT.frameAnthracite);
        addPart(0.06, legH, 0.06, -w * 0.45, legH * 0.5, d - 0.08, MAT.frameAnthracite);
        addPart(0.06, legH, 0.06, w * 0.45, legH * 0.5, d - 0.08, MAT.frameAnthracite);
        return group;
    }

    if (furn.assetId === 'furniture_sofa_220') {
        addPart(w, 0.42, d, 0, 0.21, d * 0.5, MAT.sofaFabric);
        addPart(w, h, 0.22, 0, h * 0.5, 0.11, MAT.sofaFabric);
        addPart(0.18, h * 0.75, d, -w * 0.5 + 0.09, h * 0.375, d * 0.5, MAT.cabinetFront);
        addPart(0.18, h * 0.75, d, w * 0.5 - 0.09, h * 0.375, d * 0.5, MAT.cabinetFront);
        return group;
    }

    // Kitchen Cabinet / Appliance (Bottom-Back-Center Pivot: X in [-w/2, w/2], Y in [0, h], Z in [0, d])
    const hasTop = ['cabinet_base_60', 'cabinet_sink_80', 'appliance_dishwasher_60'].includes(furn.assetId);
    const topH = hasTop ? 0.038 : 0;
    const bodyH = h - topH;
    const isInox = ['appliance_dishwasher_60', 'appliance_fridge_60'].includes(furn.assetId);

    addPart(w - 0.004, bodyH, d - 0.02, 0, bodyH * 0.5, (d - 0.02) * 0.5, isInox ? MAT.inoxMetal : MAT.cabinetBody);
    if (hasTop) {
        addPart(w, topH, d + 0.02, 0, bodyH + topH * 0.5, (d + 0.02) * 0.5, MAT.countertopOak);
    }

    if (furn.assetId === 'cabinet_sink_80') {
        addPart(w * 0.7, 0.015, d * 0.62, 0, h + 0.005, d * 0.52, MAT.inoxMetal);
        addPart(0.035, 0.26, 0.035, 0, h + 0.13, 0.12, MAT.inoxMetal);
        addPart(0.03, 0.03, 0.18, 0, h + 0.25, 0.20, MAT.inoxMetal);
    }

    if (cat.hasDoor) {
        const hinge = new THREE.Group();
        hinge.position.set(-w * 0.5 + 0.01, 0, d - 0.02);
        group.add(hinge);

        const doorFront = addPart(w - 0.008, bodyH - 0.008, 0.02, w * 0.5 - 0.01, bodyH * 0.5, 0.01, isInox ? MAT.inoxMetal : MAT.cabinetFront, hinge);
        addPart(0.02, 0.14, 0.025, w * 0.85, bodyH * 0.75, 0.025, MAT.inoxMetal, hinge);

        const animState = { hinge, isOpen: false, targetAngle: 0, maxAngle: -Math.PI * 0.45 };
        animatedDoors.push(animState);
        doorFront.userData.interactable = {
            getPrompt: () => animState.isOpen ? `[LPM / E] Zamknij: ${cat.name}` : `[LPM / E] Otwórz: ${cat.name}`,
            interact: () => {
                animState.isOpen = !animState.isOpen;
                animState.targetAngle = animState.isOpen ? animState.maxAngle : 0;
            }
        };
        walkthroughInteractables.push(doorFront);
    }
    return group;
}

function playSwitchClickSound() {
    try {
        const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
        if (audioCtx.state === 'suspended') {
            audioCtx.resume();
        }
        const osc = audioCtx.createOscillator();
        const gain = audioCtx.createGain();
        osc.type = 'triangle';
        osc.frequency.setValueAtTime(850, audioCtx.currentTime);
        osc.frequency.exponentialRampToValueAtTime(140, audioCtx.currentTime + 0.035);
        gain.gain.setValueAtTime(0.25, audioCtx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.005, audioCtx.currentTime + 0.035);
        osc.connect(gain);
        gain.connect(audioCtx.destination);
        osc.start();
        osc.stop(audioCtx.currentTime + 0.04);
    } catch (_) {}
}

function createMEP3DFixture(mep) {
    const group = new THREE.Group();
    group.position.set(mep.worldPos.x, mep.worldPos.y, mep.worldPos.z);

    const n = new THREE.Vector3(mep.normal.x, mep.normal.y, mep.normal.z).normalize();
    const target = group.position.clone().add(n);
    group.lookAt(target);

    if (mep.category === 'CEILING_LIGHT') {
        const housing = new THREE.Mesh(new THREE.BoxGeometry(0.28, 0.28, 0.04), MAT.mepWhite);
        housing.position.z = 0.02;
        const bulbMat = MAT.mepEmissiveOn.clone();
        bulbMat.color.setHex(isRoomLightingOn ? 0xffffff : 0x212529);
        bulbMat.emissive.setHex(isRoomLightingOn ? 0xffea75 : 0x000000);
        bulbMat.emissiveIntensity = isRoomLightingOn ? 4.0 : 0.0;
        const bulb = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.22, 0.01), bulbMat);
        bulb.position.z = 0.042;
        group.add(housing, bulb);

        const ptLight = new THREE.PointLight(0xffe8b5, isRoomLightingOn ? 7.5 : 0.0, 22.0, 1.0);
        ptLight.position.set(0, 0, 0.15);
        ptLight.visible = isRoomLightingOn;
        group.add(ptLight);

        group.userData.lightRef = { light: ptLight, bulbMat, on: isRoomLightingOn };
        return group;
    }

    if (mep.category === 'SWITCH_SINGLE') {
        const bezel = new THREE.Mesh(new THREE.BoxGeometry(0.085, 0.085, 0.016), MAT.mepWhite);
        bezel.position.z = 0.008;
        const rocker = new THREE.Mesh(new THREE.BoxGeometry(0.052, 0.052, 0.012), MAT.mepWhite);
        rocker.position.z = 0.016;
        rocker.rotation.x = isRoomLightingOn ? 0.12 : -0.12;

        // Generous invisible hit-box (32cm x 32cm) so crosshair/mouse easily detects the switch
        const hitBox = new THREE.Mesh(
            new THREE.BoxGeometry(0.32, 0.32, 0.16),
            new THREE.MeshBasicMaterial({ visible: false, transparent: true, opacity: 0 })
        );
        hitBox.position.z = 0.05;
        group.add(bezel, rocker, hitBox);

        const interactObj = {
            getPrompt: () => isRoomLightingOn ? '[LPM / E] Zgaś oświetlenie (Włącznik ścienny)' : '[LPM / E] Włącz oświetlenie (Włącznik ścienny)',
            interact: () => {
                isRoomLightingOn = !isRoomLightingOn;
                rocker.rotation.x = isRoomLightingOn ? 0.12 : -0.12;

                // 1. Linked lights from switchLightMap
                let linked = switchLightMap.get(mep.id) || [];

                // 2. Fallback: if switch has no explicit mepLinks, toggle all ceiling lights on this floor
                if (linked.length === 0) {
                    const fl = getActiveFloor();
                    const allFloorLights = [];
                    for (const cl of (fl.ceilingLights || [])) {
                        if (cl._lightRef) allFloorLights.push(cl._lightRef);
                    }
                    linked = allFloorLights;
                }

                // 3. Apply state to all linked fixtures
                for (const item of linked) {
                    item.on = isRoomLightingOn;
                    item.light.visible = isRoomLightingOn;
                    item.light.intensity = isRoomLightingOn ? 7.5 : 0.0;
                    item.bulbMat.emissive.setHex(isRoomLightingOn ? 0xffea75 : 0x000000);
                    item.bulbMat.emissiveIntensity = isRoomLightingOn ? 4.0 : 0.0;
                    item.bulbMat.color.setHex(isRoomLightingOn ? 0xffffff : 0x212529);
                    item.bulbMat.needsUpdate = true;
                }

                playSwitchClickSound();
                const count = linked.length;
                setTelemetry(isRoomLightingOn ? `💡 Włącznik: Zaświecono oświetlenie (${count} ${count === 1 ? 'punkt' : 'punkty/ów'}).` : `🌑 Włącznik: Zgaszono oświetlenie (${count} ${count === 1 ? 'punkt' : 'punkty/ów'}).`);
            }
        };

        hitBox.userData.interactable = interactObj;
        bezel.userData.interactable = interactObj;
        rocker.userData.interactable = interactObj;
        group.userData.interactable = interactObj;

        walkthroughInteractables.push(hitBox, bezel, rocker);
        return group;
    }

    let mat = MAT.mepWhite;
    let sz = 0.082;
    if (mep.category === 'SOCKET_400') { mat = MAT.mepRed400; sz = 0.095; }
    else if (mep.category === 'WATER_COLD') { mat = MAT.mepBlueCold; sz = 0.055; }
    else if (mep.category === 'WATER_HOT') { mat = MAT.mepRedHot; sz = 0.055; }
    else if (mep.category === 'SEWAGE_50') { mat = MAT.mepSewage; sz = 0.065; }

    const mesh = new THREE.Mesh(new THREE.BoxGeometry(sz, sz, 0.025), mat);
    mesh.position.z = 0.012;
    group.add(mesh);
    return group;
}

function buildFloorSlabAndStats(floor, floorGroup, isActiveFloor) {
    if (floor.walls.length === 0) return;
    let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const w of floor.walls) {
        minX = Math.min(minX, w.start.x, w.end.x);
        maxX = Math.max(maxX, w.start.x, w.end.x);
        minZ = Math.min(minZ, w.start.z, w.end.z);
        maxZ = Math.max(maxZ, w.start.z, w.end.z);
    }
    const wSpan = Math.max(0.5, maxX - minX);
    const zSpan = Math.max(0.5, maxZ - minZ);
    const area = wSpan * zSpan;

    const slabGeom = new THREE.BoxGeometry(wSpan, 0.22, zSpan);
    const slabMesh = new THREE.Mesh(slabGeom, [MAT.wallEdge, MAT.wallEdge, MAT.floorParquet, MAT.wallEdge, MAT.wallEdge, MAT.wallEdge]);
    slabMesh.position.set((minX + maxX) * 0.5, -0.11, (minZ + maxZ) * 0.5);
    slabMesh.receiveShadow = true;
    floorGroup.add(slabMesh);

    // Build realistic interior ceiling in Walkthrough FPP mode to block open sky and seal the room
    if (isWalkthroughFPP) {
        const ceilGeom = new THREE.BoxGeometry(wSpan + 0.3, 0.15, zSpan + 0.3);
        const ceilMesh = new THREE.Mesh(ceilGeom, MAT.wallFront);
        ceilMesh.position.set((minX + maxX) * 0.5, floor.defaultHeight + 0.075, (minZ + maxZ) * 0.5);
        ceilMesh.castShadow = true;
        ceilMesh.receiveShadow = true;
        floorGroup.add(ceilMesh);
    }

    if (isActiveFloor) {
        const statsEl = document.getElementById('lbl-floor-stats');
        if (statsEl) {
            statsEl.textContent = `${floor.name} | Ściany: ${floor.walls.length} | Meble: ${(floor.furnitures || []).length} | Pow. obrysu: ${area.toFixed(1)} m²`;
        }
    }
}

function buildAtticRoofSlabs3D(floor, floorGroup) {
    // Roof building disabled
}

// ============================================================================
// 6. INTERACTIVE CAD TOOLS (Default: SELECT & NAVIGATE — no accidental walls!)
// ============================================================================
let activeTool = 'SELECT';
let activeCatalogId = 'cabinet_base_60';
let activeMepCategory = 'SOCKET_230';
let freeFurnitureYaw = 0;

let wallChainStart = null;
let calibrationPtA = null;
let calibrationPtB = null;
let pendingSwitchId = null;
let selectedItem = null;
let movingObject = null;
let lastHoveredWallInfo = null;

const raycaster = new THREE.Raycaster();
const mouseNDC = new THREE.Vector2();

function getFloorIntersectionXZ(clientX, clientY) {
    mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
    mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
    raycaster.setFromCamera(mouseNDC, activeCamera);

    const floorY = getActiveFloor().elevationY;
    const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -floorY);
    const target = new THREE.Vector3();
    if (raycaster.ray.intersectPlane(plane, target)) {
        return { x: target.x, y: target.y, z: target.z };
    }
    return null;
}

function resolveWallEndpointSnap(rawXZ, anchorStart, shiftHeld) {
    const floor = getActiveFloor();
    let snappedNode = false;
    for (const w of floor.walls) {
        if (dist2D(rawXZ, w.start) < 0.22) return { x: w.start.x, z: w.start.z, snappedNode: true };
        if (dist2D(rawXZ, w.end) < 0.22) return { x: w.end.x, z: w.end.z, snappedNode: true };
    }

    let res = { x: rawXZ.x, z: rawXZ.z };
    if (anchorStart) {
        const dx = res.x - anchorStart.x;
        const dz = res.z - anchorStart.z;
        const d = Math.hypot(dx, dz);
        if (d > 0.05) {
            const deg = Math.atan2(dz, dx) * 180 / Math.PI;
            const snap90 = Math.round(deg / 90) * 90;
            if (shiftHeld) {
                const snap45 = Math.round(deg / 45) * 45 * Math.PI / 180;
                res = { x: anchorStart.x + Math.cos(snap45) * d, z: anchorStart.z + Math.sin(snap45) * d };
            } else if (Math.abs(deg - snap90) < 3.0) {
                const rad = snap90 * Math.PI / 180;
                res = { x: anchorStart.x + Math.cos(rad) * d, z: anchorStart.z + Math.sin(rad) * d };
            }
        }
    }
    res.x = Math.round(res.x / 0.05) * 0.05;
    res.z = Math.round(res.z / 0.05) * 0.05;
    return { ...res, snappedNode };
}

function clearPreviews() {
    while (previewGroup.children.length > 0) previewGroup.remove(previewGroup.children[0]);
    dimLineHoriz.visible = false;
    dimLineVert.visible = false;
    document.getElementById('floating-dim-badge').style.display = 'none';
}

function showFloatingBadge(clientX, clientY, text) {
    const badge = document.getElementById('floating-dim-badge');
    badge.style.display = 'block';
    badge.style.left = `${clientX + 16}px`;
    badge.style.top = `${clientY - 12}px`;
    badge.textContent = text;
}

// ============================================================================
// 7. VIEWPORT POINTER & KEYBOARD EVENT HANDLERS
// ============================================================================
let isDraggingOrbit = false;
let isDraggingPan = false;
let prevMouse = { x: 0, y: 0 };
let mouseDownPos = { x: 0, y: 0 };
let hasDraggedSinceDown = false;
let pendingClickButton = -1;

canvas.addEventListener('contextmenu', e => e.preventDefault());

canvas.addEventListener('mousedown', e => {
    if (isWalkthroughFPP) {
        if (e.button === 0) {
            triggerWalkthroughInteraction(e.clientX, e.clientY);
        }
        if (document.pointerLockElement !== canvas) {
            canvas.requestPointerLock();
        }
        return;
    }

    closeAllDropdownMenus();
    prevMouse = { x: e.clientX, y: e.clientY };
    mouseDownPos = { x: e.clientX, y: e.clientY };
    hasDraggedSinceDown = false;
    pendingClickButton = -1;

    // Middle mouse button for Camera Pan
    if (e.button === 1) {
        isDraggingPan = true;
        return;
    }

    // Alt+LMB always orbits/pans
    if (e.button === 0 && e.altKey) {
        if (!is2DOrthoMode && !isWallElevation2DMode) isDraggingOrbit = true;
        else isDraggingPan = true;
        return;
    }

    // Right-Click cancels active wall chain immediately, otherwise orbits on drag or deletes on stationary click
    if (e.button === 2) {
        if (isDraggingWallEndpoint) {
            cancelDraggingWallEndpoint();
            return;
        }
        if (activeTool === 'MOVE_OBJECT') {
            cancelMovingObject();
            return;
        }
        if (wallChainStart) {
            wallChainStart = null;
            clearPreviews();
            setTelemetry('Zakończono łańcuch rysowania ścian.');
            return;
        }
        pendingClickButton = 2;
        if (!is2DOrthoMode && !isWallElevation2DMode) isDraggingOrbit = true;
        else isDraggingPan = true;
        return;
    }

    if (e.button === 0) {
        // 1. Check if clicking on Wall Endpoint Handle
        if (wallEndpointHandles.length > 0) {
            mouseNDC.x = (e.clientX / window.innerWidth) * 2 - 1;
            mouseNDC.y = -(e.clientY / window.innerHeight) * 2 + 1;
            raycaster.setFromCamera(mouseNDC, activeCamera);
            const handleHits = raycaster.intersectObjects(wallEndpointHandles, true);
            if (handleHits.length > 0) {
                let hObj = handleHits[0].object;
                while (hObj && !hObj.userData.kind) hObj = hObj.parent;
                if (hObj && hObj.userData.kind === 'WALL_HANDLE') {
                    startDraggingWallEndpoint(hObj.userData.wall, hObj.userData.endpoint, e.clientX, e.clientY);
                    return;
                }
            }
        }

        // In SELECT mode (default), dragging LMB smoothly orbits/pans the camera; clicking without dragging selects
        if (activeTool === 'SELECT') {
            pendingClickButton = 0;
            if (!is2DOrthoMode && !isWallElevation2DMode) isDraggingOrbit = true;
            else isDraggingPan = true;
            return;
        }
        handleLeftClickToolAction(e.clientX, e.clientY, e.shiftKey);
    }
});

window.addEventListener('mousemove', e => {
    if (isWalkthroughFPP) {
        if (document.pointerLockElement === canvas) {
            fppState.yaw -= e.movementX * 0.0022;
            fppState.pitch = Math.max(-1.42, Math.min(1.42, fppState.pitch - e.movementY * 0.0022));
        }
        return;
    }

    if (isDraggingWallEndpoint) {
        updateDraggingWallEndpoint(e.clientX, e.clientY, e.shiftKey);
        return;
    }

    const dx = e.clientX - prevMouse.x;
    const dy = e.clientY - prevMouse.y;
    prevMouse = { x: e.clientX, y: e.clientY };

    if (isDraggingOrbit || isDraggingPan) {
        const distFromDown = Math.hypot(e.clientX - mouseDownPos.x, e.clientY - mouseDownPos.y);
        if (distFromDown > 4) {
            hasDraggedSinceDown = true;
        }
    }

    if (isDraggingOrbit) {
        if (!hasDraggedSinceDown && pendingClickButton !== -1) return;
        camOrbit.yaw -= dx * 0.006;
        camOrbit.pitch = Math.max(0.15, Math.min(1.48, camOrbit.pitch + dy * 0.006));
        updateCameraTransform();
        return;
    }

    if (isDraggingPan) {
        if (!hasDraggedSinceDown && pendingClickButton !== -1) return;
        const panScale = (is2DOrthoMode || isWallElevation2DMode) ? camOrbit.orthoScale * 0.0025 : camOrbit.distance * 0.0015;
        const right = new THREE.Vector3(1, 0, 0).applyQuaternion(activeCamera.quaternion);
        const up = new THREE.Vector3(0, 1, 0).applyQuaternion(activeCamera.quaternion);
        camOrbit.target.addScaledVector(right, -dx * panScale);
        if (isWallElevation2DMode) {
            camOrbit.target.addScaledVector(up, dy * panScale);
        } else {
            const forwardPlanar = new THREE.Vector3(up.x, 0, up.z).normalize();
            camOrbit.target.addScaledVector(forwardPlanar, dy * panScale);
        }
        updateCameraTransform();
        return;
    }

    handleToolHoverPreview(e.clientX, e.clientY, e.shiftKey);
});

window.addEventListener('mouseup', e => {
    if (isDraggingWallEndpoint) {
        finishDraggingWallEndpoint();
        return;
    }
    if (!isWalkthroughFPP && !hasDraggedSinceDown && pendingClickButton !== -1) {
        if (pendingClickButton === 0 && activeTool === 'SELECT') {
            handleLeftClickToolAction(e.clientX, e.clientY, e.shiftKey);
        } else if (pendingClickButton === 2) {
            handleRightClickContextMenu(e.clientX, e.clientY);
        }
    }
    pendingClickButton = -1;
    hasDraggedSinceDown = false;
    isDraggingOrbit = false;
    isDraggingPan = false;
});

canvas.addEventListener('wheel', e => {
    if (isWalkthroughFPP) return;
    e.preventDefault();
    const factor = e.deltaY > 0 ? 1.1 : 0.9;
    if (is2DOrthoMode || isWallElevation2DMode) {
        camOrbit.orthoScale = Math.max(1.2, Math.min(35, camOrbit.orthoScale * factor));
    } else {
        camOrbit.distance = Math.max(2.0, Math.min(45, camOrbit.distance * factor));
    }
    updateCameraTransform();
}, { passive: false });

function handleToolHoverPreview(clientX, clientY, shiftHeld) {
    clearPreviews();
    const floor = getActiveFloor();

    // Check hover over wall endpoint handles when selecting
    if (wallEndpointHandles.length > 0 && !isDraggingOrbit && !isDraggingPan) {
        mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
        mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
        raycaster.setFromCamera(mouseNDC, activeCamera);
        const handleHits = raycaster.intersectObjects(wallEndpointHandles, true);
        if (handleHits.length > 0) {
            let hObj = handleHits[0].object;
            while (hObj && !hObj.userData.kind) hObj = hObj.parent;
            if (hObj && hObj.userData.kind === 'WALL_HANDLE') {
                canvas.style.cursor = 'grab';
                if (hoveredWallHandle !== hObj) {
                    if (hoveredWallHandle?.userData?.sphereMesh) {
                        hoveredWallHandle.userData.sphereMesh.material.color.setHex(0x1971c2);
                        hoveredWallHandle.userData.sphereMesh.scale.set(1, 1, 1);
                    }
                    hoveredWallHandle = hObj;
                    if (hoveredWallHandle.userData?.sphereMesh) {
                        hoveredWallHandle.userData.sphereMesh.material.color.setHex(0xff922b);
                        hoveredWallHandle.userData.sphereMesh.scale.set(1.25, 1.25, 1.25);
                    }
                }
                const epLabel = hObj.userData.endpoint === 'start' ? 'Początek' : 'Koniec';
                showFloatingBadge(clientX, clientY, `Uchwyt: ${epLabel} ściany (przeciągnij LPM)`);
                return;
            }
        }
    }
    if (hoveredWallHandle) {
        if (hoveredWallHandle.userData?.sphereMesh) {
            hoveredWallHandle.userData.sphereMesh.material.color.setHex(0x1971c2);
            hoveredWallHandle.userData.sphereMesh.scale.set(1, 1, 1);
        }
        hoveredWallHandle = null;
        canvas.style.cursor = 'default';
        hideFloatingBadge();
    }

    if (activeTool === 'DRAW_WALL') {
        const pt = getFloorIntersectionXZ(clientX, clientY);
        if (!pt) return;
        const snapped = resolveWallEndpointSnap(pt, wallChainStart, shiftHeld);

        if (wallChainStart) {
            const len = dist2D(wallChainStart, snapped);
            const wL = document.getElementById('inp-wall-length');
            if (wL) wL.value = len.toFixed(2);
            const thick = parseFloat(document.getElementById('inp-wall-thickness').value) || 0.24;
            const h = parseFloat(document.getElementById('inp-wall-height').value) || floor.defaultHeight;
            if (len >= 0.12) {
                const tempW = { start: wallChainStart, end: snapped, thickness: thick, height: h, openings: [], clipRoof: false };
                const dir = normalize2D({ x: snapped.x - wallChainStart.x, z: snapped.z - wallChainStart.z });
                const n = { x: -dir.z, z: dir.x };
                const ht = thick * 0.5;
                const miter = {
                    startLeft: { x: wallChainStart.x + n.x * ht, z: wallChainStart.z + n.z * ht },
                    startRight: { x: wallChainStart.x - n.x * ht, z: wallChainStart.z - n.z * ht },
                    endLeft: { x: snapped.x + n.x * ht, z: snapped.z + n.z * ht },
                    endRight: { x: snapped.x - n.x * ht, z: snapped.z - n.z * ht }
                };
                const mesh = new THREE.Mesh(buildProceduralWallGeometry(tempW, miter, []), MAT.previewValid);
                mesh.position.y = floor.elevationY;
                previewGroup.add(mesh);
                showFloatingBadge(clientX, clientY, `${len.toFixed(2)} m (${(len * 100).toFixed(0)} cm)`);
                setTelemetry(`Rysowanie ściany: Długość = ${len.toFixed(2)} m | Grubość = ${(thick * 100).toFixed(0)} cm${snapped.snappedNode ? ' [PRZYCIĄGNIĘTO DO WĘZŁA]' : ''}`);
            }
        }
        return;
    }

    if (activeTool === 'MOVE_OBJECT' && movingObject) {
        if (movingObject.type === 'FURNITURE') {
            const snap = computeCabinetSnapping(clientX, clientY, movingObject.furnRef);
            if (!snap) return;
            movingObject.currentSnap = snap;
            const pGroup = createFurniture3DInstance(movingObject.furnRef);
            pGroup.position.copy(snap.pos);
            pGroup.rotation.y = snap.rotY;
            previewGroup.add(pGroup);

            const cat = CATALOG_ITEMS[movingObject.furnRef.assetId] || { name: 'Mebel' };
            setTelemetry(`✥ Przesuwanie: ${cat.name} | ${snap.edgeSnapped ? '[PRZYCIĄGNIĘTO BOK-DO-BOKU]' : snap.wallId ? '[PRZYCIĄGNIĘTO DO ŚCIANY]' : '[WOLNOSTOJĄCY | R = Obróć]'} (Kliknij LPM, aby postawić, Esc = anuluj)`);
            return;
        }

        if (movingObject.type === 'OPENING') {
            mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
            mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
            raycaster.setFromCamera(mouseNDC, activeCamera);
            const hits = raycaster.intersectObjects(wallPickMeshes, false);
            if (hits.length === 0) return;

            const hit = hits[0];
            const { wall, miter } = hit.object.userData;
            const wallLen = dist2D(wall.start, wall.end);
            const op = movingObject.opRef;

            let along = (hit.point.x - wall.start.x) * miter.dir.x + (hit.point.z - wall.start.z) * miter.dir.z;
            if (Math.abs(along - wallLen * 0.5) < 0.14) along = wallLen * 0.5;
            else along = Math.round(along / 0.05) * 0.05;

            const valid = along - op.width * 0.5 >= 0.10 && along + op.width * 0.5 <= wallLen - 0.10;
            const box = new THREE.Mesh(new THREE.BoxGeometry(op.width, op.height, wall.thickness + 0.06), valid ? MAT.previewValid : MAT.previewInvalid);
            box.position.set(
                wall.start.x + miter.dir.x * along,
                getActiveFloor().elevationY + op.sill + op.height * 0.5,
                wall.start.z + miter.dir.z * along
            );
            box.rotation.y = Math.atan2(miter.normal.x, miter.normal.z);
            previewGroup.add(box);

            const leftDist = Math.max(0, along - op.width * 0.5);
            const rightDist = Math.max(0, wallLen - (along + op.width * 0.5));
            showFloatingBadge(clientX, clientY, `L: ${leftDist.toFixed(2)}m | P: ${rightDist.toFixed(2)}m`);
            setTelemetry(`✥ Przesuwanie stolarki (${op.type === 'DOOR' ? 'Drzwi' : 'Okno'}): Od lewej = ${leftDist.toFixed(2)} m | Od prawej = ${rightDist.toFixed(2)} m (LPM = zatwierdź, Esc = anuluj)`);
            movingObject.currentWallId = wall.id;
            movingObject.currentAlong = along;
            movingObject.valid = valid;
            return;
        }

        if (movingObject.type === 'MEP') {
            const mepSnap = computeMEPSnapping(clientX, clientY);
            if (!mepSnap) return;
            movingObject.currentMepSnap = mepSnap;
            const previewCube = new THREE.Mesh(
                new THREE.BoxGeometry(0.11, 0.11, 0.04),
                mepSnap.hasHazard ? MAT.previewInvalid : MAT.previewValid
            );
            previewCube.position.copy(mepSnap.worldPos);
            previewCube.lookAt(mepSnap.worldPos.clone().add(mepSnap.normal));
            previewGroup.add(previewCube);
            setTelemetry(`✥ Przesuwanie MEP: Od narożnika = ${(mepSnap.cornerDist * 100).toFixed(0)} cm | Wys = ${(mepSnap.elev * 100).toFixed(0)} cm (LPM = postaw, Esc = anuluj)`);
            return;
        }

        if (movingObject.type === 'CEILING_LIGHT') {
            const pt = getFloorIntersectionXZ(clientX, clientY);
            if (!pt) return;
            movingObject.currentClPos = pt;
            const fl = getActiveFloor();
            const elev = fl.defaultHeight - 0.02;
            const previewCube = new THREE.Mesh(new THREE.CylinderGeometry(0.12, 0.12, 0.04, 16), MAT.previewValid);
            previewCube.position.set(Math.round(pt.x / 0.1) * 0.1, fl.elevationY + elev, Math.round(pt.z / 0.1) * 0.1);
            previewGroup.add(previewCube);
            setTelemetry(`✥ Przesuwanie lampy sufitowej: X = ${(pt.x).toFixed(2)} m, Z = ${(pt.z).toFixed(2)} m (LPM = postaw, Esc = anuluj)`);
            return;
        }

        if (movingObject.type === 'WALL') {
            const pt = getFloorIntersectionXZ(clientX, clientY);
            if (!pt) return;
            if (!movingObject.basePt) {
                movingObject.basePt = { ...pt };
            }
            const delta = {
                x: Math.round((pt.x - movingObject.basePt.x) / 0.05) * 0.05,
                z: Math.round((pt.z - movingObject.basePt.z) / 0.05) * 0.05
            };
            movingObject.currentDelta = delta;
            const w = movingObject.wallRef;
            const newStart = { x: w.start.x + delta.x, z: w.start.z + delta.z };
            const newEnd = { x: w.end.x + delta.x, z: w.end.z + delta.z };
            const dir = normalize2D({ x: newEnd.x - newStart.x, z: newEnd.z - newStart.z });
            const n = { x: -dir.z, z: dir.x };
            const ht = w.thickness * 0.5;
            const miter = {
                startLeft: { x: newStart.x + n.x * ht, z: newStart.z + n.z * ht },
                startRight: { x: newStart.x - n.x * ht, z: newStart.z - n.z * ht },
                endLeft: { x: newEnd.x + n.x * ht, z: newEnd.z + n.z * ht },
                endRight: { x: newEnd.x - n.x * ht, z: newEnd.z - n.z * ht }
            };
            const tempW = { start: newStart, end: newEnd, thickness: w.thickness, height: w.height, openings: [], clipRoof: false };
            const mesh = new THREE.Mesh(buildProceduralWallGeometry(tempW, miter, []), MAT.previewValid);
            mesh.position.y = getActiveFloor().elevationY;
            previewGroup.add(mesh);
            setTelemetry(`✥ Przesuwanie ściany: Przesunięcie ΔX = ${(delta.x).toFixed(2)} m, ΔZ = ${(delta.z).toFixed(2)} m (LPM = zatwierdź, Esc = anuluj)`);
            return;
        }

        if (movingObject.type === 'COLUMN') {
            const pt = getFloorIntersectionXZ(clientX, clientY);
            if (!pt) return;
            const c = movingObject.colRef;
            const snapX = Math.round(pt.x / 0.05) * 0.05;
            const snapZ = Math.round(pt.z / 0.05) * 0.05;
            movingObject.currentColPos = { x: snapX, z: snapZ };
            const previewCol = createColumn3DInstance({ ...c, pos: { x: snapX, z: snapZ } }, getActiveFloor().defaultHeight);
            previewCol.position.set(snapX, getActiveFloor().elevationY, snapZ);
            previewGroup.add(previewCol);
            setTelemetry(`✥ Przesuwanie słupa: X = ${snapX.toFixed(2)} m, Z = ${snapZ.toFixed(2)} m (LPM = postaw, Esc = anuluj)`);
            return;
        }
    }

    if (activeTool === 'PLACE_WINDOW' || activeTool === 'PLACE_DOOR' || activeTool === 'PLACE_GARAGE_DOOR') {
        mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
        mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
        raycaster.setFromCamera(mouseNDC, activeCamera);
        const hits = raycaster.intersectObjects(wallPickMeshes, false);
        if (hits.length === 0) return;

        const hit = hits[0];
        const { wall, miter } = hit.object.userData;
        const wallLen = dist2D(wall.start, wall.end);
        let w, h, sill;
        if (activeTool === 'PLACE_GARAGE_DOOR') {
            w = parseFloat(document.getElementById('inp-garage-width')?.value) || 5.00;
            h = parseFloat(document.getElementById('inp-garage-height')?.value) || 2.15;
            sill = 0.0;
        } else {
            w = parseFloat(document.getElementById('inp-open-width').value) || 1.20;
            h = parseFloat(document.getElementById('inp-open-height').value) || 1.50;
            sill = activeTool === 'PLACE_DOOR' ? 0.0 : (parseFloat(document.getElementById('inp-open-sill').value) || 0.85);
        }

        let along = (hit.point.x - wall.start.x) * miter.dir.x + (hit.point.z - wall.start.z) * miter.dir.z;
        if (Math.abs(along - wallLen * 0.5) < 0.14) along = wallLen * 0.5;
        else along = Math.round(along / 0.05) * 0.05;

        const valid = along - w * 0.5 >= 0.10 && along + w * 0.5 <= wallLen - 0.10;
        const box = new THREE.Mesh(new THREE.BoxGeometry(w, h, wall.thickness + 0.08), valid ? MAT.previewValid : MAT.previewInvalid);
        box.position.set(
            wall.start.x + miter.dir.x * along,
            floor.elevationY + sill + h * 0.5,
            wall.start.z + miter.dir.z * along
        );
        box.rotation.y = Math.atan2(miter.normal.x, miter.normal.z);
        previewGroup.add(box);

        const leftDist = Math.max(0, along - w * 0.5);
        const rightDist = Math.max(0, wallLen - (along + w * 0.5));
        showFloatingBadge(clientX, clientY, `L: ${leftDist.toFixed(2)}m | P: ${rightDist.toFixed(2)}m`);
        const opLabel = activeTool === 'PLACE_GARAGE_DOOR' ? 'Brama garażowa' : activeTool === 'PLACE_DOOR' ? 'Drzwi' : 'Okno';
        setTelemetry(`${opLabel}: Od lewego narożnika = ${leftDist.toFixed(2)} m | Od prawego = ${rightDist.toFixed(2)} m`);
        return;
    }

    if (activeTool === 'PLACE_FURNITURE') {
        const snap = computeCabinetSnapping(clientX, clientY);
        if (!snap) return;
        const cat = CATALOG_ITEMS[activeCatalogId] || CATALOG_ITEMS.cabinet_base_60;
        const box = new THREE.Mesh(new THREE.BoxGeometry(cat.size.x, cat.size.y, cat.size.z), MAT.previewValid);
        box.position.set(0, cat.size.y * 0.5, cat.size.z * 0.5);
        const holder = new THREE.Group();
        holder.add(box);
        holder.position.copy(snap.pos);
        holder.rotation.y = snap.rotY;
        previewGroup.add(holder);

        setTelemetry(`Mebel: ${cat.name} | ${snap.edgeSnapped ? '[PRZYCIĄGNIĘTO BOK-DO-BOKU (Edge-to-Edge)]' : snap.wallId ? '[PRZYCIĄGNIĘTO TYŁEM DO ŚCIANY]' : '[WOLNOSTOJĄCY | R = Obróć]'}`);
        return;
    }

    if (activeTool === 'PLACE_MEP') {
        const mepSnap = computeMEPSnapping(clientX, clientY);
        if (!mepSnap) return;

        lastHoveredWallInfo = mepSnap;
        const previewCube = new THREE.Mesh(
            new THREE.BoxGeometry(0.11, 0.11, 0.04),
            mepSnap.hasHazard ? MAT.previewInvalid : MAT.previewValid
        );
        previewCube.position.copy(mepSnap.worldPos);
        previewCube.lookAt(mepSnap.worldPos.clone().add(mepSnap.normal));
        previewGroup.add(previewCube);

        if (mepSnap.cornerWorld) {
            dimLineHoriz.geometry.setFromPoints([mepSnap.worldPos, mepSnap.cornerWorld]);
            dimLineHoriz.visible = true;
            dimLineVert.geometry.setFromPoints([mepSnap.worldPos, mepSnap.floorProjWorld]);
            dimLineVert.visible = true;
            showFloatingBadge(clientX, clientY, `Narożnik: ${(mepSnap.cornerDist * 100).toFixed(0)} cm | Wys: ${(mepSnap.elev * 100).toFixed(0)} cm`);
        }

        setTelemetry(
            `MEP [${activeMepCategory}] | Od narożnika: ${(mepSnap.cornerDist * 100).toFixed(0)} cm | Od podłogi: ${(mepSnap.elev * 100).toFixed(0)} cm` +
            (mepSnap.hasHazard ? ' ⚠️ UWAGA: KOLIZJA ZE STREFĄ ZMYWARKI/ZLEWU LUB SKOSEM DACHU!' : ' | [V = Widok 2D Ściany]'),
            mepSnap.hasHazard
        );
    }

    if (activeTool === 'PLACE_COLUMN') {
        const pt = getFloorIntersectionXZ(clientX, clientY);
        if (!pt) return;
        const colW = parseFloat(document.getElementById('inp-col-width')?.value) || 0.30;
        const colD = parseFloat(document.getElementById('inp-col-depth')?.value) || colW;
        const colH = parseFloat(document.getElementById('inp-col-height')?.value) || floor.defaultHeight;
        const isRound = !!document.getElementById('chk-col-round')?.checked;
        const snapX = Math.round(pt.x / 0.05) * 0.05;
        const snapZ = Math.round(pt.z / 0.05) * 0.05;

        const geom = isRound ? new THREE.CylinderGeometry(colW * 0.5, colW * 0.5, colH, 24) : new THREE.BoxGeometry(colW, colH, colD);
        const mesh = new THREE.Mesh(geom, MAT.previewValid);
        mesh.position.set(snapX, floor.elevationY + colH * 0.5, snapZ);
        previewGroup.add(mesh);
        setTelemetry(`🏛️ Wstawianie słupa: ${Math.round(colW * 100)}×${Math.round(colD * 100)} cm | Poz: X=${snapX.toFixed(2)}m, Z=${snapZ.toFixed(2)}m (LPM = wstaw)`);
        return;
    }
}

function computeCabinetSnapping(clientX, clientY, customFurn = null) {
    const floor = getActiveFloor();
    const assetId = customFurn ? customFurn.assetId : activeCatalogId;
    const cat = CATALOG_ITEMS[assetId] || CATALOG_ITEMS.cabinet_base_60;
    const furnSize = (customFurn && customFurn.customSize) ? customFurn.customSize : cat.size;
    const mountY = floor.elevationY + (cat.mountY || 0);

    mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
    mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
    raycaster.setFromCamera(mouseNDC, activeCamera);

    const wallHits = raycaster.intersectObjects(wallPickMeshes, false);
    if (wallHits.length > 0 && cat.snapWall) {
        const hit = wallHits[0];
        const { wall, miter } = hit.object.userData;
        const faceSign = ((hit.point.x - wall.start.x) * miter.normal.x + (hit.point.z - wall.start.z) * miter.normal.z) >= 0 ? 1 : -1;
        const faceNormal = { x: miter.normal.x * faceSign, z: miter.normal.z * faceSign };
        const rotY = Math.atan2(faceNormal.x, faceNormal.z);
        const tangent = { x: -faceNormal.z, z: faceNormal.x };

        let candidatePos = new THREE.Vector3(hit.point.x, mountY, hit.point.z);
        let edgeSnapped = false;

        // Edge-to-Edge Snapping with neighboring cabinets on the same wall
        for (const other of (floor.furnitures || [])) {
            if (other.wallId !== wall.id) continue;
            if (customFurn && other.id === customFurn.id) continue;
            const otherCat = CATALOG_ITEMS[other.assetId] || CATALOG_ITEMS.cabinet_base_60;
            const otherSize = other.customSize || otherCat.size;
            const dx = candidatePos.x - other.pos.x;
            const dz = candidatePos.z - other.pos.z;
            const alongT = dx * tangent.x + dz * tangent.z;
            const targetDist = (otherSize.x + furnSize.x) * 0.5;

            if (Math.abs(alongT - targetDist) < 0.14) {
                candidatePos.x = other.pos.x + tangent.x * targetDist;
                candidatePos.z = other.pos.z + tangent.z * targetDist;
                edgeSnapped = true;
                break;
            }
            if (Math.abs(alongT + targetDist) < 0.14) {
                candidatePos.x = other.pos.x - tangent.x * targetDist;
                candidatePos.z = other.pos.z - tangent.z * targetDist;
                edgeSnapped = true;
                break;
            }
        }
        return { pos: candidatePos, rotY, wallId: wall.id, edgeSnapped };
    }

    const pt = getFloorIntersectionXZ(clientX, clientY);
    if (!pt) return null;
    return {
        pos: new THREE.Vector3(Math.round(pt.x / 0.05) * 0.05, mountY, Math.round(pt.z / 0.05) * 0.05),
        rotY: (customFurn && customFurn.rotY !== undefined) ? customFurn.rotY : freeFurnitureYaw,
        wallId: null,
        edgeSnapped: false
    };
}

function computeMEPSnapping(clientX, clientY) {
    const floor = getActiveFloor();
    mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
    mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
    raycaster.setFromCamera(mouseNDC, activeCamera);

    if (activeMepCategory === 'CEILING_LIGHT') {
        const pt = getFloorIntersectionXZ(clientX, clientY);
        if (!pt) return null;
        const elev = floor.defaultHeight - 0.02;
        return {
            isCeiling: true,
            worldPos: new THREE.Vector3(Math.round(pt.x / 0.1) * 0.1, floor.elevationY + elev, Math.round(pt.z / 0.1) * 0.1),
            normal: new THREE.Vector3(0, -1, 0),
            elev,
            cornerDist: 0,
            hasHazard: false
        };
    }

    const hits = raycaster.intersectObjects(wallPickMeshes, false);
    if (hits.length === 0) return null;

    const hit = hits[0];
    const { wall, miter } = hit.object.userData;
    const elev = parseFloat(document.getElementById('inp-mep-elevation').value) || 0.30;
    const worldY = floor.elevationY + elev;

    const faceSign = ((hit.point.x - wall.start.x) * miter.normal.x + (hit.point.z - wall.start.z) * miter.normal.z) >= 0 ? 1 : -1;
    const normal = new THREE.Vector3(miter.normal.x * faceSign, 0, miter.normal.z * faceSign);
    const worldPos = new THREE.Vector3(hit.point.x, worldY, hit.point.z).addScaledVector(normal, 0.005);

    const dStart = dist2D({ x: worldPos.x, z: worldPos.z }, wall.start);
    const dEnd = dist2D({ x: worldPos.x, z: worldPos.z }, wall.end);
    const nearestCorner = dStart <= dEnd ? wall.start : wall.end;
    const cornerDist = Math.min(dStart, dEnd);

    // Collision Check: Appliance Hazard Zone (Dishwasher / Sink) & Attic Roof Slope
    let hasHazard = false;
    if (activeMepCategory === 'SOCKET_230' || activeMepCategory === 'SOCKET_400') {
        for (const f of (floor.furnitures || [])) {
            if (!f.hazard) continue;
            if (dist2D({ x: worldPos.x, z: worldPos.z }, f.pos) < 0.42 && elev <= 0.85) {
                hasHazard = true;
            }
        }
    }
    if (floor.isAttic) {
        const planes = buildAtticRoofPlanes(floor);
        for (const p of planes) {
            const d = p.nx * (worldPos.x - p.ox) + p.ny * (elev - p.oy) + p.nz * (worldPos.z - p.oz);
            if (d < 0.05) hasHazard = true;
        }
    }

    return {
        isCeiling: false,
        wall,
        worldPos,
        normal,
        elev,
        cornerDist,
        cornerWorld: new THREE.Vector3(nearestCorner.x, worldY, nearestCorner.z),
        floorProjWorld: new THREE.Vector3(worldPos.x, floor.elevationY, worldPos.z),
        hasHazard
    };
}

function handleLeftClickToolAction(clientX, clientY, shiftHeld) {
    const floor = getActiveFloor();

    if (activeTool === 'DRAW_WALL') {
        const pt = getFloorIntersectionXZ(clientX, clientY);
        if (!pt) return;
        const snapped = resolveWallEndpointSnap(pt, wallChainStart, shiftHeld);
        if (!wallChainStart) {
            wallChainStart = { x: snapped.x, z: snapped.z };
        } else if (dist2D(wallChainStart, snapped) >= 0.15) {
            const thick = parseFloat(document.getElementById('inp-wall-thickness').value) || 0.24;
            const h = parseFloat(document.getElementById('inp-wall-height').value) || floor.defaultHeight;
            const startPt = { ...wallChainStart };
            const endPt = { x: snapped.x, z: snapped.z };
            executeWithUndo('Dodaj ścianę', () => {
                getActiveFloor().walls.push({
                    id: nextId('w'),
                    start: startPt,
                    end: endPt,
                    thickness: thick,
                    height: h,
                    clipRoof: false,
                    openings: [],
                    mep: []
                });
            });
            wallChainStart = snapped.snappedNode ? null : endPt;
            clearPreviews();
        }
        return;
    }

    if (activeTool === 'MOVE_OBJECT') {
        commitMovingObject(clientX, clientY);
        return;
    }

    if (activeTool === 'PLACE_COLUMN') {
        const pt = getFloorIntersectionXZ(clientX, clientY);
        if (!pt) return;
        const colW = parseFloat(document.getElementById('inp-col-width')?.value) || 0.30;
        const colD = parseFloat(document.getElementById('inp-col-depth')?.value) || colW;
        const colH = parseFloat(document.getElementById('inp-col-height')?.value) || floor.defaultHeight;
        const isRound = !!document.getElementById('chk-col-round')?.checked;
        const snapX = Math.round(pt.x / 0.05) * 0.05;
        const snapZ = Math.round(pt.z / 0.05) * 0.05;

        executeWithUndo('Wstaw słup konstrukcyjny', () => {
            const fl = getActiveFloor();
            if (!fl.columns) fl.columns = [];
            fl.columns.push({
                id: nextId('col'),
                pos: { x: snapX, z: snapZ },
                width: colW,
                depth: colD,
                height: colH,
                round: isRound
            });
        });
        return;
    }

    if (activeTool === 'PLACE_WINDOW' || activeTool === 'PLACE_DOOR' || activeTool === 'PLACE_GARAGE_DOOR') {
        mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
        mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
        raycaster.setFromCamera(mouseNDC, activeCamera);
        const hits = raycaster.intersectObjects(wallPickMeshes, false);
        if (hits.length === 0) return;
        const hit = hits[0];
        const { wall, miter } = hit.object.userData;
        const wallLen = dist2D(wall.start, wall.end);
        let w, h, sill, opType, opColor;
        if (activeTool === 'PLACE_GARAGE_DOOR') {
            w = parseFloat(document.getElementById('inp-garage-width')?.value) || 5.00;
            h = parseFloat(document.getElementById('inp-garage-height')?.value) || 2.15;
            sill = 0.0;
            opType = 'GARAGE_DOOR';
            opColor = document.getElementById('sel-garage-color')?.value || 'anthracite';
        } else {
            w = parseFloat(document.getElementById('inp-open-width').value) || 1.20;
            h = parseFloat(document.getElementById('inp-open-height').value) || 1.50;
            sill = activeTool === 'PLACE_DOOR' ? 0.0 : (parseFloat(document.getElementById('inp-open-sill').value) || 0.85);
            opType = activeTool === 'PLACE_DOOR' ? 'DOOR' : 'WINDOW';
            opColor = 'anthracite';
        }

        let along = (hit.point.x - wall.start.x) * miter.dir.x + (hit.point.z - wall.start.z) * miter.dir.z;
        if (Math.abs(along - wallLen * 0.5) < 0.14) along = wallLen * 0.5;
        else along = Math.round(along / 0.05) * 0.05;

        if (along - w * 0.5 < 0.10 || along + w * 0.5 > wallLen - 0.10) return;

        const actionName = opType === 'GARAGE_DOOR' ? 'Wstaw bramę garażową' : opType === 'DOOR' ? 'Wstaw drzwi' : 'Wstaw okno';
        executeWithUndo(actionName, () => {
            const targetW = getActiveFloor().walls.find(x => x.id === wall.id);
            if (targetW) {
                targetW.openings.push({
                    id: nextId('op'),
                    type: opType,
                    dist: along,
                    width: w,
                    height: h,
                    sill,
                    color: opColor,
                    flip: false
                });
            }
        });
        return;
    }

    if (activeTool === 'PLACE_FURNITURE') {
        const snap = computeCabinetSnapping(clientX, clientY);
        if (!snap) return;
        const cat = CATALOG_ITEMS[activeCatalogId] || CATALOG_ITEMS.cabinet_base_60;
        executeWithUndo(`Wstaw: ${cat.name}`, () => {
            getActiveFloor().furnitures.push({
                id: nextId('furn'),
                assetId: activeCatalogId,
                pos: { x: snap.pos.x, y: snap.pos.y - getActiveFloor().elevationY, z: snap.pos.z },
                rotY: snap.rotY,
                wallId: snap.wallId,
                hazard: cat.hazard
            });
        });
        return;
    }

    if (activeTool === 'PLACE_MEP') {
        const mepSnap = computeMEPSnapping(clientX, clientY);
        if (!mepSnap) return;
        executeWithUndo(`Osadź MEP: ${activeMepCategory}`, () => {
            const fl = getActiveFloor();
            if (mepSnap.isCeiling) {
                fl.ceilingLights.push({
                    id: nextId('light'),
                    category: 'CEILING_LIGHT',
                    worldPos: { x: mepSnap.worldPos.x, y: mepSnap.worldPos.y - fl.elevationY, z: mepSnap.worldPos.z },
                    normal: { x: 0, y: -1, z: 0 }
                });
            } else {
                const targetW = fl.walls.find(x => x.id === mepSnap.wall.id);
                if (targetW) {
                    targetW.mep.push({
                        id: nextId('mep'),
                        category: activeMepCategory,
                        dist: mepSnap.cornerDist,
                        elev: mepSnap.elev,
                        worldPos: { x: mepSnap.worldPos.x, y: mepSnap.worldPos.y - fl.elevationY, z: mepSnap.worldPos.z },
                        normal: { x: mepSnap.normal.x, y: 0, z: mepSnap.normal.z }
                    });
                }
            }
        });
        return;
    }

    if (activeTool === 'LINK_MEP') {
        mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
        mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
        raycaster.setFromCamera(mouseNDC, activeCamera);
        const hits = raycaster.intersectObjects(selectableObjects, true);
        for (const h of hits) {
            let curr = h.object;
            while (curr && !curr.userData.kind) curr = curr.parent;
            if (!curr) continue;
            if (curr.userData.kind === 'MEP' && curr.userData.mep.category === 'SWITCH_SINGLE') {
                pendingSwitchId = curr.userData.mep.id;
                setTelemetry(`Wybrano Włącznik [${pendingSwitchId}]. Teraz kliknij LPM na Lampę Sufitową LED!`);
                return;
            }
            if (pendingSwitchId && curr.userData.kind === 'CEILING_LIGHT') {
                const swId = pendingSwitchId;
                const ltId = curr.userData.mep.id;
                pendingSwitchId = null;
                executeWithUndo('Połącz włącznik z lampą', () => {
                    getActiveFloor().mepLinks.push({ id: nextId('lnk'), switchId: swId, lightId: ltId });
                });
                setTelemetry('Połączono włącznik ścienny z punktem świetlnym! Sprawdź działanie w trybie Spaceru FPP (Tab).');
                return;
            }
        }
        return;
    }

    if (activeTool === 'CALIBRATE') {
        const pt = getFloorIntersectionXZ(clientX, clientY);
        if (!pt) return;
        if (!calibrationPtA) {
            calibrationPtA = pt;
            setTelemetry('Kalibracja Rzutu 2D: Kliknij drugi punkt na linii wymiarowej rzutu.');
        } else {
            calibrationPtB = pt;
            const d = dist2D(calibrationPtA, calibrationPtB);
            setTelemetry(`Zmierzono odcinek (${d.toFixed(2)} jedn.). Wpisz wymiar rzeczywisty (m) po lewej i kliknij "Zatwierdź Skalę".`);
        }
        return;
    }

    if (activeTool === 'SELECT') {
        mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
        mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
        raycaster.setFromCamera(mouseNDC, activeCamera);
        const hits = raycaster.intersectObjects(selectableObjects, true);
        if (hits.length > 0) {
            let curr = hits[0].object;
            while (curr && !curr.userData.kind) curr = curr.parent;
            if (curr) {
                handleSelectionByObject(curr);
                return;
            }
        }
        selectedItem = null;
        rebuildEntireScene();
        updateActiveToolSummary();
    }
}

function handleSelectionByObject(curr) {
    const { kind, wall, opening, furn, mep } = curr.userData;
    if (kind === 'WALL') {
        selectedItem = { type: 'WALL', id: wall.id };
        const wThick = document.getElementById('inp-wall-thickness');
        const wH = document.getElementById('inp-wall-height');
        const wL = document.getElementById('inp-wall-length');
        if (wThick) wThick.value = wall.thickness;
        if (wH) wH.value = wall.height;
        const curLen = dist2D(wall.start, wall.end);
        if (wL) wL.value = curLen.toFixed(2);
        setTelemetry(`Zaznaczono ścianę [${wall.id}] | Długość: ${curLen.toFixed(2)} m | Grubość: ${(wall.thickness * 100).toFixed(0)} cm (Chwyć uchwyt na końcu ściany aby ją rozciągnąć/skrócić)`);
    } else if (kind === 'OPENING') {
        selectedItem = { type: 'OPENING', id: opening.id, wallId: wall.id, opening };
        if (opening.type === 'GARAGE_DOOR') {
            const gw = document.getElementById('inp-garage-width');
            const gh = document.getElementById('inp-garage-height');
            const gc = document.getElementById('sel-garage-color');
            if (gw) gw.value = opening.width;
            if (gh) gh.value = opening.height;
            if (gc && opening.color) gc.value = opening.color;
            setTelemetry(`Zaznaczono bramę garażową [${opening.id}] | Wymiar: ${(opening.width * 100).toFixed(0)}×${(opening.height * 100).toFixed(0)} cm (Wymiary możesz edytować w pasku opcji lub Delete)`);
        } else {
            const wInput = document.getElementById('inp-open-width');
            const hInput = document.getElementById('inp-open-height');
            const sInput = document.getElementById('inp-open-sill');
            if (wInput) wInput.value = opening.width;
            if (hInput) hInput.value = opening.height;
            if (sInput) sInput.value = opening.sill;
            setTelemetry(`Zaznaczono stolarkę (${opening.type === 'DOOR' ? 'Drzwi' : 'Okno'}) [${opening.id}] | Wymiar: ${(opening.width * 100).toFixed(0)}×${(opening.height * 100).toFixed(0)} cm (Wymiary możesz edytować w pasku opcji lub Delete)`);
        }
    } else if (kind === 'FURNITURE') {
        selectedItem = { type: 'FURNITURE', id: furn.id };
        const cat = CATALOG_ITEMS[furn.assetId] || { name: furn.assetId, size: { x: 0.6, y: 0.85, z: 0.6 } };
        const size = furn.customSize || cat.size;
        const nameEl = document.getElementById('opt-furn-name');
        const wEl = document.getElementById('inp-furn-dim-x');
        const zEl = document.getElementById('inp-furn-dim-z');
        const yEl = document.getElementById('inp-furn-dim-y');
        const rotEl = document.getElementById('inp-furn-rot');
        if (nameEl) nameEl.textContent = cat.name;
        if (wEl) wEl.value = size.x.toFixed(2);
        if (zEl) zEl.value = size.z.toFixed(2);
        if (yEl) yEl.value = size.y.toFixed(2);
        if (rotEl) rotEl.value = String(Math.round(((furn.rotY || 0) * 180 / Math.PI) % 360));
        setTelemetry(`Zaznaczono mebel: ${cat.name} [${furn.id}] | Wymiary: ${(size.x * 100).toFixed(0)}×${(size.z * 100).toFixed(0)}×${(size.y * 100).toFixed(0)} cm (Wymiary możesz edytować w pasku opcji lub Delete)`);
    } else if (kind === 'MEP') {
        selectedItem = { type: 'MEP', id: mep.id, wallId: wall.id };
        setTelemetry(`Zaznaczono punkt MEP: ${mep.category} [${mep.id}] (Delete / 🗑️ Usuń)`);
    } else if (kind === 'CEILING_LIGHT') {
        selectedItem = { type: 'CEILING_LIGHT', id: mep.id };
        setTelemetry(`Zaznaczono lampę sufitową LED [${mep.id}] (Delete / 🗑️ Usuń)`);
    } else if (kind === 'COLUMN') {
        const col = curr.userData.column;
        selectedItem = { type: 'COLUMN', id: col.id };
        const wInp = document.getElementById('inp-col-width');
        const dInp = document.getElementById('inp-col-depth');
        const hInp = document.getElementById('inp-col-height');
        const rInp = document.getElementById('chk-col-round');
        if (wInp) wInp.value = (col.width || 0.30).toFixed(2);
        if (dInp) dInp.value = (col.depth || col.width || 0.30).toFixed(2);
        if (hInp) hInp.value = (col.height || 2.75).toFixed(2);
        if (rInp) rInp.checked = !!col.round;
        setTelemetry(`Zaznaczono słup konstrukcyjny [${col.id}] | Wymiary: ${Math.round((col.width || 0.3)*100)}×${Math.round((col.depth || col.width || 0.3)*100)} cm, H=${(col.height || 2.75).toFixed(2)}m (M: przesuń, Delete: usuń)`);
    }
    rebuildEntireScene();
    updateActiveToolSummary();
}

function handleRightClickContextMenu(clientX, clientY) {
    if (isWalkthroughFPP) return;

    mouseNDC.x = (clientX / window.innerWidth) * 2 - 1;
    mouseNDC.y = -(clientY / window.innerHeight) * 2 + 1;
    raycaster.setFromCamera(mouseNDC, activeCamera);
    const hits = raycaster.intersectObjects(selectableObjects, true);

    if (hits.length > 0) {
        let curr = hits[0].object;
        while (curr && !curr.userData.kind) curr = curr.parent;
        if (curr) {
            handleSelectionByObject(curr);
        }
    }

    if (!selectedItem) {
        hideContextMenu();
        return;
    }

    const menu = document.getElementById('cad-context-menu');
    if (!menu) return;

    const titleEl = document.getElementById('ctx-header-title');
    let itemTitle = selectedItem.type;
    if (selectedItem.type === 'WALL') itemTitle = '🧱 ŚCIANA';
    else if (selectedItem.type === 'OPENING') itemTitle = '🪟 STOLARKA';
    else if (selectedItem.type === 'FURNITURE') {
        const f = getActiveFloor().furnitures?.find(x => x.id === selectedItem.id);
        const cat = f ? CATALOG_ITEMS[f.assetId] : null;
        itemTitle = cat ? `🛋️ ${cat.name.toUpperCase()}` : '🛋️ MEBEL';
    } else if (selectedItem.type === 'MEP' || selectedItem.type === 'CEILING_LIGHT') {
        itemTitle = '⚡ PUNKT MEP';
    } else if (selectedItem.type === 'COLUMN') {
        itemTitle = '🏛️ SŁUP / PODPORA';
    }
    if (titleEl) titleEl.textContent = itemTitle;

    const rotItem = document.getElementById('ctx-action-rotate');
    if (rotItem) rotItem.style.display = selectedItem.type === 'FURNITURE' ? 'flex' : 'none';

    const moveItem = document.getElementById('ctx-action-move');
    if (moveItem) moveItem.style.display = 'flex';

    // Constrain position to viewport
    const menuWidth = 195;
    const menuHeight = 185;
    const posX = Math.min(clientX, window.innerWidth - menuWidth - 8);
    const posY = Math.min(clientY, window.innerHeight - menuHeight - 8);

    menu.style.left = `${posX}px`;
    menu.style.top = `${posY}px`;
    menu.style.display = 'flex';
}

function hideContextMenu() {
    const menu = document.getElementById('cad-context-menu');
    if (menu) menu.style.display = 'none';
}

function startMovingSelectedItem() {
    if (!selectedItem) return;
    const item = selectedItem;
    const floor = getActiveFloor();
    hideContextMenu();

    if (item.type === 'FURNITURE') {
        const furn = floor.furnitures?.find(x => x.id === item.id);
        if (!furn) return;
        const cat = CATALOG_ITEMS[furn.assetId] || { name: 'Mebel', size: { x: 0.6, y: 0.85, z: 0.6 } };
        movingObject = {
            type: 'FURNITURE',
            id: furn.id,
            furnRef: furn,
            origPos: { ...furn.pos },
            origRotY: furn.rotY || 0,
            origWallId: furn.wallId || null,
            currentSnap: null
        };
        activeCatalogId = furn.assetId;
        freeFurnitureYaw = furn.rotY || 0;
        selectTool('MOVE_OBJECT');
        rebuildEntireScene();
        setTelemetry(`✥ Przesuwanie mebla: "${cat.name}". Kliknij LPM w nowym miejscu (R = obróć o 45°, Esc = anuluj).`);
    } else if (item.type === 'OPENING') {
        const w = floor.walls.find(x => x.id === item.wallId);
        const op = w?.openings?.find(x => x.id === item.id);
        if (!op) return;
        movingObject = {
            type: 'OPENING',
            id: op.id,
            opRef: op,
            origWallId: item.wallId,
            origDist: op.dist,
            currentWallId: item.wallId,
            currentAlong: op.dist,
            valid: true
        };
        selectTool('MOVE_OBJECT');
        rebuildEntireScene();
        setTelemetry(`✥ Przesuwanie stolarki (${op.type === 'DOOR' ? 'Drzwi' : 'Okno'}). Najedź na ścianę i kliknij LPM, aby zatwierdzić (Esc = anuluj).`);
    } else if (item.type === 'MEP') {
        const w = floor.walls.find(x => x.id === item.wallId);
        const m = w?.mep?.find(x => x.id === item.id);
        if (!m) return;
        movingObject = {
            type: 'MEP',
            id: m.id,
            mepRef: m,
            origWallId: item.wallId,
            origDist: m.dist,
            origElev: m.elev,
            origWorldPos: { ...m.worldPos },
            origNormal: { ...m.normal }
        };
        activeMepCategory = m.category;
        selectTool('MOVE_OBJECT');
        rebuildEntireScene();
        setTelemetry(`✥ Przesuwanie punktu MEP: ${m.category}. Najedź na ścianę i kliknij LPM (Esc = anuluj).`);
    } else if (item.type === 'CEILING_LIGHT') {
        const cl = floor.ceilingLights?.find(x => x.id === item.id);
        if (!cl) return;
        movingObject = {
            type: 'CEILING_LIGHT',
            id: cl.id,
            clRef: cl,
            origWorldPos: { ...cl.worldPos }
        };
        activeMepCategory = 'CEILING_LIGHT';
        selectTool('MOVE_OBJECT');
        rebuildEntireScene();
        setTelemetry(`✥ Przesuwanie lampy sufitowej. Kliknij LPM na rzucie w nowym punkcie (Esc = anuluj).`);
    } else if (item.type === 'WALL') {
        const w = floor.walls.find(x => x.id === item.id);
        if (!w) return;
        movingObject = {
            type: 'WALL',
            id: w.id,
            wallRef: w,
            origStart: { ...w.start },
            origEnd: { ...w.end },
            basePt: null,
            currentDelta: null
        };
        selectTool('MOVE_OBJECT');
        rebuildEntireScene();
        setTelemetry(`✥ Przesuwanie ściany. Przesuń kursor i kliknij LPM, aby zatwierdzić nowe położenie (Esc = anuluj).`);
    } else if (item.type === 'COLUMN') {
        const c = floor.columns?.find(x => x.id === item.id);
        if (!c) return;
        movingObject = {
            type: 'COLUMN',
            id: c.id,
            colRef: c,
            origPos: { ...c.pos },
            currentColPos: { ...c.pos }
        };
        selectTool('MOVE_OBJECT');
        rebuildEntireScene();
        setTelemetry(`✥ Przesuwanie słupa. Kliknij LPM w nowym miejscu na rzucie (Esc = anuluj).`);
    }
}

function cancelMovingObject() {
    if (!movingObject) return;
    movingObject = null;
    clearPreviews();
    rebuildEntireScene();
    selectTool('SELECT');
    setTelemetry('Anulowano przesuwanie obiektu.');
}

function commitMovingObject(clientX, clientY) {
    if (!movingObject) return;
    const fl = getActiveFloor();

    if (movingObject.type === 'FURNITURE') {
        const snap = movingObject.currentSnap || computeCabinetSnapping(clientX, clientY, movingObject.furnRef);
        if (!snap) return;
        const furnId = movingObject.id;
        const newPos = { x: snap.pos.x, y: snap.pos.y - fl.elevationY, z: snap.pos.z };
        const newRotY = snap.rotY;
        const newWallId = snap.wallId;

        executeWithUndo(`Przesuń mebel [${furnId}]`, () => {
            const f = fl.furnitures?.find(x => x.id === furnId);
            if (f) {
                f.pos = { ...newPos };
                f.rotY = newRotY;
                f.wallId = newWallId;
            }
        });
        setTelemetry(`Przesunięto mebel na nową pozycję.`);
        movingObject = null;
        clearPreviews();
        rebuildEntireScene();
        selectTool('SELECT');
        return;
    }

    if (movingObject.type === 'OPENING') {
        if (!movingObject.valid || !movingObject.currentWallId) {
            setTelemetry('Nieprawidłowa pozycja otworu na ścianie (zbyt blisko narożnika).', true);
            return;
        }
        const opId = movingObject.id;
        const oldWallId = movingObject.origWallId;
        const newWallId = movingObject.currentWallId;
        const newDist = movingObject.currentAlong;

        executeWithUndo(`Przesuń otwór [${opId}]`, () => {
            const oldW = fl.walls.find(x => x.id === oldWallId);
            const opIdx = oldW?.openings?.findIndex(x => x.id === opId);
            if (opIdx !== undefined && opIdx >= 0) {
                const [op] = oldW.openings.splice(opIdx, 1);
                op.dist = newDist;
                const newW = fl.walls.find(x => x.id === newWallId);
                if (newW) {
                    if (!newW.openings) newW.openings = [];
                    newW.openings.push(op);
                }
            }
        });
        setTelemetry(`Przesunięto otwór na nową pozycję.`);
        movingObject = null;
        clearPreviews();
        rebuildEntireScene();
        selectTool('SELECT');
        return;
    }

    if (movingObject.type === 'MEP') {
        const mepSnap = movingObject.currentMepSnap;
        if (!mepSnap) return;
        const mepId = movingObject.id;
        const oldWallId = movingObject.origWallId;

        executeWithUndo(`Przesuń punkt MEP [${mepId}]`, () => {
            const oldW = fl.walls.find(x => x.id === oldWallId);
            const idx = oldW?.mep?.findIndex(x => x.id === mepId);
            if (idx !== undefined && idx >= 0) {
                const [m] = oldW.mep.splice(idx, 1);
                m.dist = mepSnap.cornerDist;
                m.elev = mepSnap.elev;
                m.worldPos = { x: mepSnap.worldPos.x, y: mepSnap.worldPos.y - fl.elevationY, z: mepSnap.worldPos.z };
                m.normal = { x: mepSnap.normal.x, y: 0, z: mepSnap.normal.z };
                const newW = fl.walls.find(x => x.id === mepSnap.wall.id);
                if (newW) {
                    if (!newW.mep) newW.mep = [];
                    newW.mep.push(m);
                }
            }
        });
        setTelemetry(`Przesunięto punkt MEP.`);
        movingObject = null;
        clearPreviews();
        rebuildEntireScene();
        selectTool('SELECT');
        return;
    }

    if (movingObject.type === 'CEILING_LIGHT') {
        const pt = movingObject.currentClPos;
        if (!pt) return;
        const clId = movingObject.id;
        executeWithUndo(`Przesuń lampę sufitową [${clId}]`, () => {
            const cl = fl.ceilingLights?.find(x => x.id === clId);
            if (cl) {
                cl.worldPos.x = Math.round(pt.x / 0.1) * 0.1;
                cl.worldPos.z = Math.round(pt.z / 0.1) * 0.1;
            }
        });
        setTelemetry(`Przesunięto lampę sufitową.`);
        movingObject = null;
        clearPreviews();
        rebuildEntireScene();
        selectTool('SELECT');
        return;
    }

    if (movingObject.type === 'WALL') {
        const delta = movingObject.currentDelta;
        if (!delta || (Math.abs(delta.x) < 0.01 && Math.abs(delta.z) < 0.01)) {
            movingObject = null;
            clearPreviews();
            rebuildEntireScene();
            selectTool('SELECT');
            return;
        }
        const wallId = movingObject.id;
        executeWithUndo(`Przesuń ścianę [${wallId}]`, () => {
            const w = fl.walls?.find(x => x.id === wallId);
            if (w) {
                w.start.x += delta.x;
                w.start.z += delta.z;
                w.end.x += delta.x;
                w.end.z += delta.z;
            }
        });
        setTelemetry(`Przesunięto ścianę.`);
        movingObject = null;
        clearPreviews();
        rebuildEntireScene();
        selectTool('SELECT');
        return;
    }

    if (movingObject.type === 'COLUMN') {
        const pt = movingObject.currentColPos;
        if (!pt) return;
        const colId = movingObject.id;
        executeWithUndo(`Przesuń słup [${colId}]`, () => {
            const c = fl.columns?.find(x => x.id === colId);
            if (c) {
                c.pos.x = Math.round(pt.x / 0.05) * 0.05;
                c.pos.z = Math.round(pt.z / 0.05) * 0.05;
            }
        });
        setTelemetry(`Przesunięto słup konstrukcyjny.`);
        movingObject = null;
        clearPreviews();
        rebuildEntireScene();
        selectTool('SELECT');
        return;
    }
}

function requestDeleteSelectedItemWithConfirm() {
    if (!selectedItem) {
        setTelemetry('Brak zaznaczonego obiektu do usunięcia.', true);
        return;
    }
    const dialog = document.getElementById('cad-confirm-dialog');
    const msgEl = document.getElementById('confirm-dialog-msg');
    let itemLabel = selectedItem.type;
    if (selectedItem.type === 'WALL') itemLabel = 'Ściana';
    else if (selectedItem.type === 'OPENING') itemLabel = 'Stolarka otworowa (Okno / Drzwi)';
    else if (selectedItem.type === 'FURNITURE') {
        const f = getActiveFloor().furnitures?.find(x => x.id === selectedItem.id);
        const cat = f ? CATALOG_ITEMS[f.assetId] : null;
        itemLabel = cat ? `Mebel: "${cat.name}"` : 'Mebel';
    } else if (selectedItem.type === 'MEP' || selectedItem.type === 'CEILING_LIGHT') {
        itemLabel = 'Punkt instalacji MEP';
    } else if (selectedItem.type === 'COLUMN') {
        itemLabel = 'Słup konstrukcyjny / podpora';
    }
    if (msgEl) msgEl.textContent = `Czy na pewno chcesz usunąć obiekt: ${itemLabel} (ID: ${selectedItem.id})?`;
    if (dialog) dialog.classList.add('show');
}

function deleteSelectedItem() {
    if (!selectedItem) {
        setTelemetry('Brak zaznaczonego obiektu. Użyj narzędzia "🖱️ Wybierz" i kliknij obiekt lub kliknij PPM bezpośrednio na obiekcie w scenie 3D.', true);
        return;
    }
    const item = { ...selectedItem };
    selectedItem = null;
    executeWithUndo(`Usuń zaznaczony obiekt (${item.type})`, () => {
        const fl = getActiveFloor();
        if (item.type === 'WALL') {
            fl.walls = fl.walls.filter(w => w.id !== item.id);
        } else if (item.type === 'OPENING') {
            const w = fl.walls.find(x => x.id === item.wallId);
            if (w) w.openings = w.openings.filter(o => o.id !== item.id);
        } else if (item.type === 'COLUMN') {
            fl.columns = (fl.columns || []).filter(c => c.id !== item.id);
        } else if (item.type === 'FURNITURE') {
            fl.furnitures = fl.furnitures.filter(f => f.id !== item.id);
        } else if (item.type === 'MEP') {
            const w = fl.walls.find(x => x.id === item.wallId);
            if (w) w.mep = w.mep.filter(m => m.id !== item.id);
        } else if (item.type === 'CEILING_LIGHT') {
            fl.ceilingLights = fl.ceilingLights.filter(l => l.id !== item.id);
        }
    });
    rebuildEntireScene();
    updateActiveToolSummary();
    setTelemetry(`Usunięto obiekt [${item.id}].`);
}

// ============================================================================
// 8. CAMERA MODES (3D Orbit, 2D Ortho, 2D Wall Elevation & Walkthrough FPP)
// ============================================================================
function updateCameraTransform() {
    if (isWalkthroughFPP) return;
    const aspect = window.innerWidth / window.innerHeight;

    if (is2DOrthoMode) {
        activeCamera = orthoCamera;
        orthoCamera.left = -camOrbit.orthoScale * aspect;
        orthoCamera.right = camOrbit.orthoScale * aspect;
        orthoCamera.top = camOrbit.orthoScale;
        orthoCamera.bottom = -camOrbit.orthoScale;
        orthoCamera.near = 0.1;
        orthoCamera.updateProjectionMatrix();
        orthoCamera.position.set(camOrbit.target.x, 30, camOrbit.target.z);
        orthoCamera.lookAt(camOrbit.target.x, 0, camOrbit.target.z);
    } else if (isWallElevation2DMode) {
        activeCamera = orthoCamera;
        orthoCamera.left = -camOrbit.orthoScale * aspect;
        orthoCamera.right = camOrbit.orthoScale * aspect;
        orthoCamera.top = camOrbit.orthoScale;
        orthoCamera.bottom = -camOrbit.orthoScale;
        orthoCamera.near = 0.05;
        orthoCamera.updateProjectionMatrix();
    } else {
        activeCamera = perspCamera;
        perspCamera.fov = 58;
        perspCamera.updateProjectionMatrix();
        const x = camOrbit.target.x + camOrbit.distance * Math.cos(camOrbit.pitch) * Math.sin(camOrbit.yaw);
        const y = camOrbit.target.y + camOrbit.distance * Math.sin(camOrbit.pitch);
        const z = camOrbit.target.z + camOrbit.distance * Math.cos(camOrbit.pitch) * Math.cos(camOrbit.yaw);
        perspCamera.position.set(x, y, z);
        perspCamera.lookAt(camOrbit.target);
    }

    syncHudNavigationUI();
}

function syncHudNavigationUI() {
    const compassDial = document.getElementById('compass-dial');
    if (compassDial) {
        const deg = is2DOrthoMode ? 0 : (camOrbit.yaw * 180) / Math.PI;
        compassDial.style.transform = `rotate(${deg.toFixed(1)}deg)`;
        compassDial.querySelectorAll('.compass-dir-btn').forEach(btn => {
            if (btn.classList.contains('dir-n') || btn.classList.contains('dir-s')) {
                btn.style.transform = `translateX(-50%) rotate(${(-deg).toFixed(1)}deg)`;
            } else {
                btn.style.transform = `translateY(-50%) rotate(${(-deg).toFixed(1)}deg)`;
            }
        });
    }

    const centerBtn = document.getElementById('compass-center-btn');
    if (centerBtn) {
        centerBtn.textContent = (is2DOrthoMode || isWallElevation2DMode) ? '2D' : '3D';
    }

    const camBtn = document.getElementById('btn-toggle-cam');
    if (camBtn) {
        if (isWallElevation2DMode) camBtn.textContent = 'Widok: 2D Ściana';
        else if (is2DOrthoMode) camBtn.textContent = 'Widok: 2D Rzut';
        else camBtn.textContent = 'Widok: 3D Orbit';
    }

    document.getElementById('qs-view-top')?.classList.toggle('active', is2DOrthoMode);
    document.getElementById('qs-view-wall')?.classList.toggle('active', isWallElevation2DMode);
    document.getElementById('qs-view-iso')?.classList.toggle('active', !is2DOrthoMode && !isWallElevation2DMode);
}

function enterWallElevation2D() {
    const floor = getActiveFloor();
    const w = lastHoveredWallInfo?.wall || floor.walls[0];
    if (!w) return;
    const dir = normalize2D({ x: w.end.x - w.start.x, z: w.end.z - w.start.z });
    const n = lastHoveredWallInfo?.normal || new THREE.Vector3(-dir.z, 0, dir.x);
    const midX = (w.start.x + w.end.x) * 0.5;
    const midZ = (w.start.z + w.end.z) * 0.5;
    const midY = floor.elevationY + w.height * 0.5;

    isWallElevation2DMode = true;
    is2DOrthoMode = false;
    camOrbit.orthoScale = 2.2;
    camOrbit.target.set(midX, midY, midZ);
    orthoCamera.position.set(midX + n.x * 1.4, midY, midZ + n.z * 1.4);
    orthoCamera.lookAt(midX, midY, midZ);
    updateCameraTransform();
    setTelemetry('Widok 2D Rozwinięcia Ściany (Brak przesłaniania) | Kliknij "3D" na kompasie lub "Widok: 2D Ściana", aby wrócić do 3D Orbit.');
}

function closeAllDropdownMenus() {
    document.querySelectorAll('.dropdown-panel.show').forEach(panel => panel.classList.remove('show'));
    document.querySelectorAll('[data-dropdown-toggle].open').forEach(btn => btn.classList.remove('open'));
}

// Top Bar Dropdown Toggle Logic
document.querySelectorAll('[data-dropdown-toggle]').forEach(toggleBtn => {
    toggleBtn.addEventListener('click', e => {
        e.stopPropagation();
        const menuId = toggleBtn.dataset.dropdownToggle;
        const menuEl = document.getElementById(menuId);
        if (!menuEl) return;
        const isAlreadyOpen = menuEl.classList.contains('show');
        closeAllDropdownMenus();
        if (!isAlreadyOpen) {
            menuEl.classList.add('show');
            toggleBtn.classList.add('open');
        }
    });
});

// Prevent clicks inside dropdown panels from immediately closing the panel when adjusting numeric inputs/sliders
document.querySelectorAll('.dropdown-panel').forEach(panel => {
    panel.addEventListener('click', e => {
        e.stopPropagation();
    });
});

window.addEventListener('click', () => {
    closeAllDropdownMenus();
});

function toggleWalkthroughFPP() {
    isWalkthroughFPP = !isWalkthroughFPP;
    clearPreviews();
    closeAllDropdownMenus();

    const hudEl = document.getElementById('walkthrough-hud');
    if (hudEl) hudEl.style.display = isWalkthroughFPP ? 'flex' : 'none';

    const bottomLeftHud = document.getElementById('bottom-left-hud');
    if (bottomLeftHud) bottomLeftHud.style.display = isWalkthroughFPP ? 'none' : 'flex';

    const wtBtn = document.getElementById('btn-walkthrough');
    if (wtBtn) wtBtn.textContent = isWalkthroughFPP ? '⏹ Wyjdź (Tab/Esc)' : '🚶 Spacer FPP (Tab)';

    if (isWalkthroughFPP) {
        isRoomLightingOn = true;
        activeCamera = perspCamera;
        perspCamera.fov = 70; // 20mm Full-Frame architectural wide angle
        perspCamera.updateProjectionMatrix();
        fppState.pos.set(1.1, getActiveFloor().elevationY, -0.8);
        fppState.yaw = 0;
        fppState.pitch = 0;
        canvas.requestPointerLock();
        setTelemetry('TRYB SPACERU FPP | Ruch: WASD + Shift | Interakcja (Włączniki / Drzwi / Szafki): LPM lub E | Piętro: [1] Parter / [2] Poddasze | Wyjście: Tab');
    } else {
        if (document.pointerLockElement) document.exitPointerLock();
        isRoomLightingOn = true;
        applyTimeOfDay(projectData.timeOfDayHours);
        updateCameraTransform();
    }
    rebuildEntireScene();
    updateActiveToolSummary();
}

function triggerWalkthroughInteraction(clientX = null, clientY = null) {
    let ndc;
    if (clientX !== null && clientY !== null && document.pointerLockElement !== canvas) {
        ndc = new THREE.Vector2(
            (clientX / window.innerWidth) * 2 - 1,
            -(clientY / window.innerHeight) * 2 + 1
        );
    } else {
        ndc = new THREE.Vector2(0, 0);
    }
    perspCamera.updateMatrixWorld();
    raycaster.setFromCamera(ndc, perspCamera);
    const hits = raycaster.intersectObjects(walkthroughInteractables, true);
    if (hits.length > 0 && hits[0].distance <= 3.8) {
        let obj = hits[0].object;
        while (obj && !obj.userData.interactable) obj = obj.parent;
        if (obj && obj.userData.interactable) {
            obj.userData.interactable.interact();
        }
    }
}

// ============================================================================
// 9. SUN TIME-OF-DAY & REALTIME RENDER LOOP
// ============================================================================
function applyTimeOfDay(hours) {
    projectData.timeOfDayHours = hours;
    const hInt = Math.floor(hours);
    const mInt = Math.floor((hours - hInt) * 60);
    document.getElementById('lbl-time').textContent = `${String(hInt).padStart(2, '0')}:${String(mInt).padStart(2, '0')}`;
    const slider = document.getElementById('inp-time-of-day');
    if (slider && parseFloat(slider.value) !== hours) slider.value = String(hours);

    const dayNorm = (hours - 6) / 14;
    const elevRad = Math.sin(dayNorm * Math.PI) * 1.15;
    const azimRad = (dayNorm - 0.5) * Math.PI;

    if (elevRad > -0.05) {
        const r = 18;
        sunLight.position.set(
            Math.cos(elevRad) * Math.sin(azimRad) * r,
            Math.max(1.5, Math.sin(elevRad) * r),
            Math.cos(elevRad) * Math.cos(azimRad) * r
        );
        const warmFactor = Math.max(0, Math.min(1, elevRad / 0.6));
        sunLight.color.setRGB(1.0, 0.68 + 0.29 * warmFactor, 0.42 + 0.50 * warmFactor);
        sunLight.intensity = 0.5 + 1.9 * warmFactor;
        hemiLight.intensity = 0.25 + 0.50 * warmFactor;
        scene.background.setRGB(0.08 + 0.08 * warmFactor, 0.10 + 0.12 * warmFactor, 0.14 + 0.18 * warmFactor);
    } else {
        sunLight.position.set(-8, 12, -8);
        sunLight.color.setHex(0x4c6ef5);
        sunLight.intensity = 0.12;
        hemiLight.intensity = 0.12;
        scene.background.setHex(0x090b10);
    }
}

let lastFrameTime = performance.now();
function animate(now) {
    requestAnimationFrame(animate);
    const dt = Math.min(0.05, (now - lastFrameTime) * 0.001);
    lastFrameTime = now;

    // Smoothly animate hinged doors and cabinet fronts
    for (const d of animatedDoors) {
        d.hinge.rotation.y += (d.targetAngle - d.hinge.rotation.y) * Math.min(1, dt * 8);
    }

    if (isWalkthroughFPP) {
        // Dramatic room ambient dimming when room lighting is turned off
        const targetHemi = isRoomLightingOn ? 0.60 : 0.07;
        const isDaytime = (projectData.timeOfDayHours >= 6 && projectData.timeOfDayHours <= 20);
        const baseSun = isDaytime ? 1.8 : 0.12;
        const targetSun = isRoomLightingOn ? baseSun : Math.min(baseSun, 0.20);
        hemiLight.intensity += (targetHemi - hemiLight.intensity) * Math.min(1, dt * 10);
        sunLight.intensity += (targetSun - sunLight.intensity) * Math.min(1, dt * 10);

        const speed = (fppState.keys.shift ? 4.8 : 3.1) * dt;
        const fwd = new THREE.Vector3(-Math.sin(fppState.yaw), 0, -Math.cos(fppState.yaw));
        const right = new THREE.Vector3(Math.cos(fppState.yaw), 0, -Math.sin(fppState.yaw));

        if (fppState.keys.w) fppState.pos.addScaledVector(fwd, speed);
        if (fppState.keys.s) fppState.pos.addScaledVector(fwd, -speed);
        if (fppState.keys.d) fppState.pos.addScaledVector(right, speed);
        if (fppState.keys.a) fppState.pos.addScaledVector(right, -speed);

        perspCamera.position.set(fppState.pos.x, fppState.pos.y + fppState.eyeHeight, fppState.pos.z);
        const euler = new THREE.Euler(fppState.pitch, fppState.yaw, 0, 'YXZ');
        perspCamera.quaternion.setFromEuler(euler);
        perspCamera.updateMatrixWorld();

        // Center-screen hover prompt check
        raycaster.setFromCamera(new THREE.Vector2(0, 0), perspCamera);
        const hits = raycaster.intersectObjects(walkthroughInteractables, true);
        const promptEl = document.getElementById('interaction-prompt');
        if (hits.length > 0 && hits[0].distance <= 3.8) {
            let obj = hits[0].object;
            while (obj && !obj.userData.interactable) obj = obj.parent;
            if (obj && obj.userData.interactable) {
                promptEl.textContent = obj.userData.interactable.getPrompt();
                promptEl.style.display = 'block';
            } else promptEl.style.display = 'none';
        } else {
            promptEl.style.display = 'none';
        }
    }

    renderer.render(scene, activeCamera);
}

// ============================================================================
// 10. TOP NAVIGATION BAR DROPDOWNS, BOTTOM-LEFT HUD & C# HOST BRIDGE
// ============================================================================
function setTelemetry(msg, isWarning = false) {
    const el = document.getElementById('telemetry-text');
    el.textContent = msg;
    el.className = isWarning ? 'warning' : '';
}

function updateUndoRedoUI() {
    document.getElementById('btn-undo').disabled = commandHistory.undoStack.length === 0;
    document.getElementById('btn-redo').disabled = commandHistory.redoStack.length === 0;
}

const MEP_LABELS = {
    SOCKET_230: 'Gniazdko 230V',
    SOCKET_400: 'Siła 400V',
    SWITCH_SINGLE: 'Włącznik Światła',
    CEILING_LIGHT: 'Lampa Sufitowa LED',
    WATER_COLD: 'Woda Zimna',
    WATER_HOT: 'Woda Ciepła',
    SEWAGE_50: 'Odpływ Kan. 50mm'
};

function updateActiveToolSummary() {
    const summaryEl = document.getElementById('lbl-active-tool-summary');
    if (!summaryEl) return;

    const catWalls = document.getElementById('cat-btn-walls');
    const catOpenings = document.getElementById('cat-btn-openings');
    const catFurniture = document.getElementById('cat-btn-furniture');
    const catMep = document.getElementById('cat-btn-mep');

    if (catWalls) catWalls.classList.toggle('cat-active', activeTool === 'DRAW_WALL' || activeTool === 'CALIBRATE');
    if (catOpenings) catOpenings.classList.toggle('cat-active', activeTool === 'PLACE_WINDOW' || activeTool === 'PLACE_DOOR');
    if (catFurniture) catFurniture.classList.toggle('cat-active', activeTool === 'PLACE_FURNITURE');
    if (catMep) catMep.classList.toggle('cat-active', activeTool === 'PLACE_MEP' || activeTool === 'LINK_MEP');

    document.getElementById('qs-select')?.classList.toggle('active', activeTool === 'SELECT');
    document.getElementById('qs-draw-wall')?.classList.toggle('active', activeTool === 'DRAW_WALL');
    document.getElementById('qs-place-column')?.classList.toggle('active', activeTool === 'PLACE_COLUMN');
    document.getElementById('tool-column')?.classList.toggle('active', activeTool === 'PLACE_COLUMN');
    document.getElementById('qs-place-garage-door')?.classList.toggle('active', activeTool === 'PLACE_GARAGE_DOOR');
    document.getElementById('tool-garage-door')?.classList.toggle('active', activeTool === 'PLACE_GARAGE_DOOR');

    // Update Revit Options Bar visibility & content
    const optWall = document.getElementById('opt-wall');
    const optOpening = document.getElementById('opt-opening');
    const optGarageDoor = document.getElementById('opt-garage-door');
    const optColumn = document.getElementById('opt-column');
    const optFurniture = document.getElementById('opt-furniture');
    const optMep = document.getElementById('opt-mep');
    const optCalibrate = document.getElementById('opt-calibrate');
    const optSelect = document.getElementById('opt-select');

    const isWallActive = activeTool === 'DRAW_WALL' || selectedItem?.type === 'WALL';
    const isGarageDoorActive = activeTool === 'PLACE_GARAGE_DOOR' || (selectedItem?.type === 'OPENING' && selectedItem?.opening?.type === 'GARAGE_DOOR');
    const isOpeningActive = (activeTool === 'PLACE_WINDOW' || activeTool === 'PLACE_DOOR' || selectedItem?.type === 'OPENING') && !isGarageDoorActive;
    const isColumnActive = activeTool === 'PLACE_COLUMN' || selectedItem?.type === 'COLUMN';
    const isFurnitureActive = selectedItem?.type === 'FURNITURE';
    const isMepActive = activeTool === 'PLACE_MEP' || activeTool === 'LINK_MEP' || selectedItem?.type === 'MEP' || selectedItem?.type === 'CEILING_LIGHT';
    const isCalibrateActive = activeTool === 'CALIBRATE';
    const isSelectActive = !isWallActive && !isOpeningActive && !isGarageDoorActive && !isColumnActive && !isFurnitureActive && !isMepActive && !isCalibrateActive;

    if (optWall) optWall.classList.toggle('active', isWallActive);
    if (optOpening) optOpening.classList.toggle('active', isOpeningActive);
    if (optGarageDoor) optGarageDoor.classList.toggle('active', isGarageDoorActive);
    if (optColumn) optColumn.classList.toggle('active', isColumnActive);
    if (optFurniture) optFurniture.classList.toggle('active', isFurnitureActive);
    if (optMep) {
        optMep.classList.toggle('active', isMepActive);
        const mepLbl = document.getElementById('opt-mep-type-label');
        if (mepLbl) mepLbl.textContent = `Typ: ${MEP_LABELS[activeMepCategory] || activeMepCategory}`;
    }
    if (optCalibrate) optCalibrate.classList.toggle('active', isCalibrateActive);
    if (optSelect) {
        optSelect.classList.toggle('active', isSelectActive);
        const selInfo = document.getElementById('opt-selection-info');
        if (selInfo) {
            selInfo.textContent = selectedItem ? `Zaznaczono: ${selectedItem.type} [${selectedItem.id.slice(0, 8)}]` : 'Brak zaznaczenia';
        }
    }

    if (isWalkthroughFPP) {
        summaryEl.textContent = 'Spacer 3D FPP';
        return;
    }

    switch (activeTool) {
        case 'DRAW_WALL': {
            const th = Math.round((parseFloat(document.getElementById('inp-wall-thickness')?.value) || 0.24) * 100);
            summaryEl.textContent = `Ściana (${th} cm)`;
            break;
        }
        case 'CALIBRATE':
            summaryEl.textContent = 'Kalibracja Rzutu 2D';
            break;
        case 'PLACE_COLUMN': {
            const cw = Math.round((parseFloat(document.getElementById('inp-col-width')?.value) || 0.3) * 100);
            const cd = Math.round((parseFloat(document.getElementById('inp-col-depth')?.value) || 0.3) * 100);
            summaryEl.textContent = `Słup (${cw}×${cd} cm)`;
            break;
        }
        case 'PLACE_WINDOW': {
            const w = Math.round((parseFloat(document.getElementById('inp-open-width')?.value) || 1.2) * 100);
            const h = Math.round((parseFloat(document.getElementById('inp-open-height')?.value) || 1.5) * 100);
            summaryEl.textContent = `Okno (${w}×${h} cm)`;
            break;
        }
        case 'PLACE_DOOR': {
            const w = Math.round((parseFloat(document.getElementById('inp-open-width')?.value) || 0.9) * 100);
            const h = Math.round((parseFloat(document.getElementById('inp-open-height')?.value) || 2.05) * 100);
            summaryEl.textContent = `Drzwi (${w}×${h} cm)`;
            break;
        }
        case 'PLACE_GARAGE_DOOR': {
            const gw = Math.round((parseFloat(document.getElementById('inp-garage-width')?.value) || 5.0) * 100);
            const gh = Math.round((parseFloat(document.getElementById('inp-garage-height')?.value) || 2.15) * 100);
            summaryEl.textContent = `Brama (${gw}×${gh} cm)`;
            break;
        }
        case 'PLACE_FURNITURE': {
            const cat = CATALOG_ITEMS[activeCatalogId] || CATALOG_ITEMS.cabinet_base_60;
            summaryEl.textContent = cat.name;
            break;
        }
        case 'PLACE_MEP': {
            const lbl = MEP_LABELS[activeMepCategory] || activeMepCategory;
            summaryEl.textContent = `MEP: ${lbl}`;
            break;
        }
        case 'LINK_MEP':
            summaryEl.textContent = 'Łączenie Włącznik → Lampa';
            break;
        case 'MOVE_OBJECT':
            summaryEl.textContent = 'Przesuwanie Obiektu';
            break;
        case 'SELECT':
            summaryEl.textContent = selectedItem ? `Zaznaczono: ${selectedItem.type}` : 'Wybierz / Nawiguj';
            break;
        default:
            summaryEl.textContent = activeTool;
    }
}

function selectTool(toolName) {
    if (activeTool === 'MOVE_OBJECT' && toolName !== 'MOVE_OBJECT' && movingObject) {
        movingObject = null;
        rebuildEntireScene();
    }
    activeTool = toolName;
    wallChainStart = null;
    pendingSwitchId = null;
    clearPreviews();
    document.querySelectorAll('[data-tool]').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.tool === toolName);
    });

    if (toolName === 'SELECT') {
        setTelemetry('Gotowy | Tryb: Wybierz i Nawiguj 3D (Przeciągnij LPM = Obrót kamery, Kliknij LPM = Zaznacz obiekt)');
    } else if (toolName === 'DRAW_WALL') {
        setTelemetry('Narzędzie: Rysowanie Ścian (LPM: postaw narożnik, PPM/Esc: zakończ łańcuch, Shift: kąt 45°)');
    } else if (toolName === 'PLACE_COLUMN') {
        setTelemetry('Narzędzie: Wstawianie Słupa / Podpory (LPM: postaw słup, Esc: zakończ)');
    } else if (toolName === 'PLACE_GARAGE_DOOR') {
        setTelemetry('Narzędzie: Wstawianie Bramy Garażowej (LPM: osadź na ścianie, Esc: zakończ)');
    } else if (toolName === 'MOVE_OBJECT') {
        setTelemetry('Tryb: Przesuwanie Obiektu (LPM: zatwierdź nowe położenie, Esc: anuluj, R: obróć mebel)');
    }

    updateActiveToolSummary();
}

document.querySelectorAll('[data-tool]').forEach(btn => {
    btn.addEventListener('click', () => {
        selectTool(btn.dataset.tool);
        closeAllDropdownMenus();
    });
});

document.getElementById('btn-undo').addEventListener('click', () => commandHistory.undo());
document.getElementById('btn-redo').addEventListener('click', () => commandHistory.redo());
document.getElementById('btn-delete-selected').addEventListener('click', requestDeleteSelectedItemWithConfirm);

document.getElementById('btn-floor-0').addEventListener('click', () => switchFloor(0));
document.getElementById('btn-floor-1').addEventListener('click', () => switchFloor(1));

function switchFloor(idx) {
    if (idx < 0 || idx >= projectData.floors.length) return;
    projectData.activeFloorIndex = idx;
    document.getElementById('btn-floor-0')?.classList.toggle('active', idx === 0);
    document.getElementById('btn-floor-1')?.classList.toggle('active', idx === 1);
    const fl = getActiveFloor();
    const lblText = fl ? `${fl.name} (${fl.elevationY >= 0 ? '+' : ''}${fl.elevationY.toFixed(1)}m)` : 'Kondygnacja';
    const activeFloorLbl = document.getElementById('lbl-active-floor-btn');
    if (activeFloorLbl) {
        activeFloorLbl.textContent = lblText;
    }
    const qatFloorName = document.getElementById('qat-floor-name');
    if (qatFloorName) {
        qatFloorName.textContent = lblText;
    }
    camOrbit.target.y = (fl ? fl.elevationY : 0) + 0.8;
    updateCameraTransform();
    rebuildEntireScene();
}

function syncAllFloorsButtonUI() {
    const allFloorsBtn = document.getElementById('btn-toggle-all-floors');
    if (allFloorsBtn) {
        allFloorsBtn.textContent = showAllFloorsInCad ? 'Piętra: Wszystkie' : 'Piętra: Tylko akt.';
        allFloorsBtn.classList.toggle('active', showAllFloorsInCad);
    }
    document.getElementById('qs-full-house')?.classList.toggle('active', showAllFloorsInCad);
}

document.getElementById('btn-toggle-all-floors')?.addEventListener('click', () => {
    showAllFloorsInCad = !showAllFloorsInCad;
    syncAllFloorsButtonUI();
    rebuildEntireScene();
});

document.getElementById('btn-toggle-cam').addEventListener('click', () => {
    if (isWallElevation2DMode) {
        isWallElevation2DMode = false;
        is2DOrthoMode = false;
    } else {
        is2DOrthoMode = !is2DOrthoMode;
    }
    updateCameraTransform();
});

// ============================================================================
// 11. BOTTOM-LEFT HUD: COMPASS, CAMERA PAN/ZOOM & QUICK SHORTCUTS
// ============================================================================
function panCameraByScreenOffset(stepRight, stepUp) {
    const panAmount = (is2DOrthoMode || isWallElevation2DMode) ? camOrbit.orthoScale * 0.18 : camOrbit.distance * 0.10;
    const right = new THREE.Vector3(1, 0, 0).applyQuaternion(activeCamera.quaternion);
    const up = new THREE.Vector3(0, 1, 0).applyQuaternion(activeCamera.quaternion);
    camOrbit.target.addScaledVector(right, stepRight * panAmount);
    if (isWallElevation2DMode) {
        camOrbit.target.addScaledVector(up, stepUp * panAmount);
    } else {
        const forwardPlanar = new THREE.Vector3(up.x, 0, up.z).normalize();
        camOrbit.target.addScaledVector(forwardPlanar, stepUp * panAmount);
    }
    updateCameraTransform();
}

function zoomCameraByFactor(factor) {
    if (is2DOrthoMode || isWallElevation2DMode) {
        camOrbit.orthoScale = Math.max(1.2, Math.min(35, camOrbit.orthoScale * factor));
    } else {
        camOrbit.distance = Math.max(2.0, Math.min(45, camOrbit.distance * factor));
    }
    updateCameraTransform();
}

function resetCameraCenterFit() {
    const floor = getActiveFloor();
    camOrbit.target.set(0.3, floor.elevationY + 0.9, -0.3);
    if (is2DOrthoMode || isWallElevation2DMode) {
        camOrbit.orthoScale = 5.5;
    } else {
        camOrbit.distance = 10.5;
    }
    updateCameraTransform();
    setTelemetry('Wyśrodkowano widok kamery na budynku.');
}

// Cardinal Directions on Compass (N, E, S, W)
document.querySelectorAll('[data-compass-dir]').forEach(btn => {
    btn.addEventListener('click', e => {
        e.stopPropagation();
        const dir = btn.dataset.compassDir;
        isWallElevation2DMode = false;
        is2DOrthoMode = false;
        if (camOrbit.pitch < 0.25 || camOrbit.pitch > 1.2) camOrbit.pitch = 0.55;
        if (dir === 'N') camOrbit.yaw = Math.PI;
        else if (dir === 'S') camOrbit.yaw = 0;
        else if (dir === 'E') camOrbit.yaw = Math.PI * 0.5;
        else if (dir === 'W') camOrbit.yaw = -Math.PI * 0.5;
        updateCameraTransform();
        setTelemetry(`Kompas: Ustawiono widok z kierunku ${dir} (3D Orbit).`);
    });
});

// Compass Center Button (2D / 3D Toggle)
document.getElementById('compass-center-btn')?.addEventListener('click', e => {
    e.stopPropagation();
    if (isWallElevation2DMode || is2DOrthoMode) {
        isWallElevation2DMode = false;
        is2DOrthoMode = false;
        if (camOrbit.pitch > 1.35) camOrbit.pitch = 0.68;
        setTelemetry('Widok: 3D Orbit (Perspektywa)');
    } else {
        is2DOrthoMode = true;
        isWallElevation2DMode = false;
        setTelemetry('Widok: 2D Rzut z góry (Ortogonalny CAD)');
    }
    updateCameraTransform();
});

// Orbit & Pitch Step Buttons around Compass
document.getElementById('nav-orbit-left')?.addEventListener('click', () => {
    is2DOrthoMode = false;
    isWallElevation2DMode = false;
    camOrbit.yaw -= Math.PI / 8;
    updateCameraTransform();
});
document.getElementById('nav-orbit-right')?.addEventListener('click', () => {
    is2DOrthoMode = false;
    isWallElevation2DMode = false;
    camOrbit.yaw += Math.PI / 8;
    updateCameraTransform();
});
document.getElementById('nav-tilt-up')?.addEventListener('click', () => {
    is2DOrthoMode = false;
    isWallElevation2DMode = false;
    camOrbit.pitch = Math.min(1.46, camOrbit.pitch + 0.14);
    updateCameraTransform();
});
document.getElementById('nav-tilt-down')?.addEventListener('click', () => {
    is2DOrthoMode = false;
    isWallElevation2DMode = false;
    camOrbit.pitch = Math.max(0.18, camOrbit.pitch - 0.14);
    updateCameraTransform();
});

// Pan D-Pad Buttons
document.getElementById('nav-pan-up')?.addEventListener('click', () => panCameraByScreenOffset(0, 1));
document.getElementById('nav-pan-down')?.addEventListener('click', () => panCameraByScreenOffset(0, -1));
document.getElementById('nav-pan-left')?.addEventListener('click', () => panCameraByScreenOffset(-1, 0));
document.getElementById('nav-pan-right')?.addEventListener('click', () => panCameraByScreenOffset(1, 0));
document.getElementById('nav-pan-reset')?.addEventListener('click', resetCameraCenterFit);

// Zoom Buttons
document.getElementById('nav-zoom-in')?.addEventListener('click', () => zoomCameraByFactor(0.84));
document.getElementById('nav-zoom-out')?.addEventListener('click', () => zoomCameraByFactor(1.19));
document.getElementById('nav-zoom-fit')?.addEventListener('click', resetCameraCenterFit);

// Quick Shortcuts Widget Buttons
document.getElementById('qs-select')?.addEventListener('click', () => selectTool('SELECT'));
document.getElementById('qs-draw-wall')?.addEventListener('click', () => selectTool('DRAW_WALL'));
document.getElementById('qs-view-iso')?.addEventListener('click', () => {
    is2DOrthoMode = false;
    isWallElevation2DMode = false;
    camOrbit.yaw = Math.PI * 0.25;
    camOrbit.pitch = 0.68;
    camOrbit.distance = 10.5;
    updateCameraTransform();
    setTelemetry('Widok: Izometria 3D (Domyślny kąt kamery)');
});
document.getElementById('qs-view-top')?.addEventListener('click', () => {
    isWallElevation2DMode = false;
    is2DOrthoMode = !is2DOrthoMode;
    updateCameraTransform();
    setTelemetry(is2DOrthoMode ? 'Widok: 2D Rzut z góry (Ortogonalny)' : 'Widok: 3D Orbit');
});
document.getElementById('qs-view-wall')?.addEventListener('click', enterWallElevation2D);
document.getElementById('qs-full-house')?.addEventListener('click', () => {
    const turnOn = !showAllFloorsInCad;
    showAllFloorsInCad = turnOn;
    syncAllFloorsButtonUI();
    rebuildEntireScene();
    setTelemetry(turnOn ? 'Widok wszystkich kondygnacji budynku' : 'Widok aktywnej kondygnacji');
});

// Collapse/Expand Bottom-Left HUD Body
document.getElementById('btn-toggle-hud-body')?.addEventListener('click', e => {
    e.stopPropagation();
    const body = document.getElementById('hud-body-content');
    if (!body) return;
    const isCollapsed = body.classList.toggle('collapsed');
    e.currentTarget.textContent = isCollapsed ? '+' : '−';
});

document.getElementById('btn-walkthrough').addEventListener('click', toggleWalkthroughFPP);
document.getElementById('btn-start-fpp-side').addEventListener('click', toggleWalkthroughFPP);
document.getElementById('inp-time-of-day').addEventListener('input', e => applyTimeOfDay(parseFloat(e.target.value)));

document.querySelectorAll('.time-preset-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        const t = parseFloat(btn.dataset.time) || 14.5;
        applyTimeOfDay(t);
    });
});

document.getElementById('btn-wall-24').addEventListener('click', () => {
    document.getElementById('inp-wall-thickness').value = '0.24';
    selectTool('DRAW_WALL');
    closeAllDropdownMenus();
});
document.getElementById('btn-wall-12').addEventListener('click', () => {
    document.getElementById('inp-wall-thickness').value = '0.12';
    selectTool('DRAW_WALL');
    closeAllDropdownMenus();
});

// Live update selected wall dimensions or active tool badge when inputs change
document.getElementById('inp-wall-thickness').addEventListener('change', e => {
    const val = Math.max(0.06, Math.min(0.80, parseFloat(e.target.value) || 0.24));
    e.target.value = String(val);
    if (selectedItem?.type === 'WALL') {
        executeWithUndo('Zmień grubość ściany', () => {
            const w = getActiveFloor().walls.find(x => x.id === selectedItem.id);
            if (w) w.thickness = val;
        });
    }
    updateActiveToolSummary();
});

document.getElementById('inp-wall-height').addEventListener('change', e => {
    const val = Math.max(0.80, Math.min(5.00, parseFloat(e.target.value) || 2.75));
    e.target.value = String(val);
    if (selectedItem?.type === 'WALL') {
        executeWithUndo('Zmień wysokość ściany', () => {
            const w = getActiveFloor().walls.find(x => x.id === selectedItem.id);
            if (w) w.height = val;
        });
    }
    updateActiveToolSummary();
});

document.getElementById('inp-wall-length')?.addEventListener('change', e => {
    const val = Math.max(0.20, Math.min(60.00, parseFloat(e.target.value) || 4.0));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'WALL') {
        const w = getActiveFloor().walls.find(x => x.id === selectedItem.id);
        if (w) {
            const curLen = dist2D(w.start, w.end);
            if (curLen > 1e-4) {
                const dirX = (w.end.x - w.start.x) / curLen;
                const dirZ = (w.end.z - w.start.z) / curLen;
                executeWithUndo('Zmień długość ściany', () => {
                    w.end.x = Math.round((w.start.x + dirX * val) * 1000) / 1000;
                    w.end.z = Math.round((w.start.z + dirZ * val) * 1000) / 1000;
                    (w.openings || []).forEach(op => {
                        if (op.dist + op.width > val) {
                            op.dist = Math.max(0.1, val - op.width - 0.1);
                        }
                    });
                });
                rebuildEntireScene();
                setTelemetry(`Zmieniono długość ściany na ${val.toFixed(2)} m.`);
            }
        }
    }
});

document.getElementById('inp-open-width').addEventListener('change', e => {
    const val = Math.max(0.40, Math.min(4.00, parseFloat(e.target.value) || 0.90));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'OPENING') {
        executeWithUndo('Zmień szerokość otworu', () => {
            const w = getActiveFloor().walls.find(x => x.id === selectedItem.wallId);
            const op = w?.openings?.find(x => x.id === selectedItem.id);
            if (op) op.width = val;
        });
        rebuildEntireScene();
    }
    updateActiveToolSummary();
});

document.getElementById('inp-open-height').addEventListener('change', e => {
    const val = Math.max(0.40, Math.min(3.50, parseFloat(e.target.value) || 1.50));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'OPENING') {
        executeWithUndo('Zmień wysokość otworu', () => {
            const w = getActiveFloor().walls.find(x => x.id === selectedItem.wallId);
            const op = w?.openings?.find(x => x.id === selectedItem.id);
            if (op) op.height = val;
        });
        rebuildEntireScene();
    }
    updateActiveToolSummary();
});

document.getElementById('inp-open-sill')?.addEventListener('change', e => {
    const val = Math.max(0.00, Math.min(2.50, parseFloat(e.target.value) || 0.00));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'OPENING') {
        executeWithUndo('Zmień wysokość parapetu', () => {
            const w = getActiveFloor().walls.find(x => x.id === selectedItem.wallId);
            const op = w?.openings?.find(x => x.id === selectedItem.id);
            if (op) op.sill = val;
        });
        rebuildEntireScene();
    }
    updateActiveToolSummary();
});

// Live update selected column dimensions or profile when inputs change
document.getElementById('inp-col-width')?.addEventListener('change', e => {
    const val = Math.max(0.10, Math.min(2.50, parseFloat(e.target.value) || 0.30));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'COLUMN') {
        executeWithUndo('Zmień szerokość słupa', () => {
            const c = (getActiveFloor().columns || []).find(x => x.id === selectedItem.id);
            if (c) c.width = val;
        });
        rebuildEntireScene();
    }
    updateActiveToolSummary();
});

document.getElementById('inp-col-depth')?.addEventListener('change', e => {
    const val = Math.max(0.10, Math.min(2.50, parseFloat(e.target.value) || 0.30));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'COLUMN') {
        executeWithUndo('Zmień głębokość słupa', () => {
            const c = (getActiveFloor().columns || []).find(x => x.id === selectedItem.id);
            if (c) c.depth = val;
        });
        rebuildEntireScene();
    }
    updateActiveToolSummary();
});

document.getElementById('inp-col-height')?.addEventListener('change', e => {
    const val = Math.max(0.50, Math.min(6.00, parseFloat(e.target.value) || 2.75));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'COLUMN') {
        executeWithUndo('Zmień wysokość słupa', () => {
            const c = (getActiveFloor().columns || []).find(x => x.id === selectedItem.id);
            if (c) c.height = val;
        });
        rebuildEntireScene();
    }
    updateActiveToolSummary();
});

document.getElementById('chk-col-round')?.addEventListener('change', e => {
    const val = !!e.target.checked;
    if (selectedItem?.type === 'COLUMN') {
        executeWithUndo('Zmień profil słupa', () => {
            const c = (getActiveFloor().columns || []).find(x => x.id === selectedItem.id);
            if (c) c.round = val;
        });
        rebuildEntireScene();
    }
});

document.getElementById('tool-column')?.addEventListener('click', () => {
    selectTool('PLACE_COLUMN');
    closeAllDropdownMenus();
});
document.getElementById('qs-place-column')?.addEventListener('click', () => {
    selectTool('PLACE_COLUMN');
    closeAllDropdownMenus();
});
document.getElementById('qs-place-garage-door')?.addEventListener('click', () => {
    selectTool('PLACE_GARAGE_DOOR');
    closeAllDropdownMenus();
});

document.getElementById('inp-garage-width')?.addEventListener('change', e => {
    const val = Math.max(1.80, Math.min(8.00, parseFloat(e.target.value) || 5.00));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'OPENING' && selectedItem?.opening?.type === 'GARAGE_DOOR') {
        executeWithUndo('Zmień szerokość bramy', () => {
            const w = getActiveFloor().walls.find(x => x.id === selectedItem.wallId);
            const op = w?.openings?.find(x => x.id === selectedItem.id);
            if (op) op.width = val;
        });
        rebuildEntireScene();
    }
    updateActiveToolSummary();
});

document.getElementById('inp-garage-height')?.addEventListener('change', e => {
    const val = Math.max(1.80, Math.min(4.00, parseFloat(e.target.value) || 2.15));
    e.target.value = val.toFixed(2);
    if (selectedItem?.type === 'OPENING' && selectedItem?.opening?.type === 'GARAGE_DOOR') {
        executeWithUndo('Zmień wysokość bramy', () => {
            const w = getActiveFloor().walls.find(x => x.id === selectedItem.wallId);
            const op = w?.openings?.find(x => x.id === selectedItem.id);
            if (op) op.height = val;
        });
        rebuildEntireScene();
    }
    updateActiveToolSummary();
});

document.getElementById('sel-garage-color')?.addEventListener('change', e => {
    const val = e.target.value || 'anthracite';
    if (selectedItem?.type === 'OPENING' && selectedItem?.opening?.type === 'GARAGE_DOOR') {
        executeWithUndo('Zmień wykończenie bramy', () => {
            const w = getActiveFloor().walls.find(x => x.id === selectedItem.wallId);
            const op = w?.openings?.find(x => x.id === selectedItem.id);
            if (op) op.color = val;
        });
        rebuildEntireScene();
    }
});

// Live editing of selected furniture dimensions and rotation
function updateSelectedFurnitureProperty(prop, value) {
    if (selectedItem?.type !== 'FURNITURE') return;
    executeWithUndo(`Edycja mebla (${prop})`, () => {
        const furn = getActiveFloor().furnitures?.find(x => x.id === selectedItem.id);
        if (!furn) return;
        const cat = CATALOG_ITEMS[furn.assetId] || { size: { x: 0.6, y: 0.85, z: 0.6 } };
        if (!furn.customSize) {
            furn.customSize = { ...cat.size };
        }
        if (prop === 'x' || prop === 'y' || prop === 'z') {
            furn.customSize[prop] = value;
        } else if (prop === 'rotY') {
            furn.rotY = value;
        }
    });
    rebuildEntireScene();
}

document.getElementById('inp-furn-dim-x')?.addEventListener('change', e => {
    const v = Math.max(0.15, Math.min(6.00, parseFloat(e.target.value) || 0.60));
    e.target.value = v.toFixed(2);
    updateSelectedFurnitureProperty('x', v);
});
document.getElementById('inp-furn-dim-z')?.addEventListener('change', e => {
    const v = Math.max(0.15, Math.min(4.00, parseFloat(e.target.value) || 0.60));
    e.target.value = v.toFixed(2);
    updateSelectedFurnitureProperty('z', v);
});
document.getElementById('inp-furn-dim-y')?.addEventListener('change', e => {
    const v = Math.max(0.15, Math.min(4.00, parseFloat(e.target.value) || 0.85));
    e.target.value = v.toFixed(2);
    updateSelectedFurnitureProperty('y', v);
});
document.getElementById('inp-furn-rot')?.addEventListener('change', e => {
    const deg = parseFloat(e.target.value) || 0;
    const rad = (deg * Math.PI) / 180;
    updateSelectedFurnitureProperty('rotY', rad);
});

document.getElementById('btn-preset-win').addEventListener('click', () => {
    document.getElementById('inp-open-width').value = '1.20';
    document.getElementById('inp-open-height').value = '1.50';
    document.getElementById('inp-open-sill').value = '0.85';
    selectTool('PLACE_WINDOW');
    closeAllDropdownMenus();
});
document.getElementById('btn-preset-door').addEventListener('click', () => {
    document.getElementById('inp-open-width').value = '0.90';
    document.getElementById('inp-open-height').value = '2.05';
    document.getElementById('inp-open-sill').value = '0.00';
    selectTool('PLACE_DOOR');
    closeAllDropdownMenus();
});

document.querySelectorAll('.catalog-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        activeCatalogId = btn.dataset.catalog;
        document.querySelectorAll('.catalog-btn').forEach(b => b.classList.toggle('active', b === btn));
        selectTool('PLACE_FURNITURE');
        closeAllDropdownMenus();
    });
});

document.querySelectorAll('.mep-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        activeMepCategory = btn.dataset.mep;
        document.getElementById('inp-mep-elevation').value = btn.dataset.elev;
        document.querySelectorAll('.mep-btn').forEach(b => b.classList.toggle('active', b === btn));
        selectTool('PLACE_MEP');
        closeAllDropdownMenus();
    });
});

document.getElementById('btn-wall-elevation-2d').addEventListener('click', () => {
    enterWallElevation2D();
    closeAllDropdownMenus();
});
document.getElementById('btn-wall-elevation-2d-env')?.addEventListener('click', () => {
    enterWallElevation2D();
    closeAllDropdownMenus();
});

document.getElementById('btn-confirm-scale').addEventListener('click', () => {
    if (!calibrationPtA || !calibrationPtB) return;
    const span = dist2D(calibrationPtA, calibrationPtB);
    const realM = parseFloat(document.getElementById('inp-calibrate-meters').value) || 5.0;
    if (span > 0.01 && realM > 0.05) {
        executeWithUndo('Kalibracja skali rzutu 2D', () => {
            getActiveFloor().blueprint.metersPerPixel *= (realM / span);
        });
        calibrationPtA = null;
        calibrationPtB = null;
        setTelemetry(`Skalibrowano rzut 2D (1 jednostka = 1 metr).`);
        closeAllDropdownMenus();
    }
});

// Keyboard Shortcuts
window.addEventListener('keydown', e => {
    if (e.target.tagName === 'INPUT') return;

    if (e.key === 'Tab') {
        e.preventDefault();
        toggleWalkthroughFPP();
        return;
    }

    if (isWalkthroughFPP) {
        const k = e.key.toLowerCase();
        if (k === 'w') fppState.keys.w = true;
        if (k === 's') fppState.keys.s = true;
        if (k === 'a') fppState.keys.a = true;
        if (k === 'd') fppState.keys.d = true;
        if (e.key === 'Shift') fppState.keys.shift = true;
        if (k === 'e') triggerWalkthroughInteraction();
        if (k === '1') fppState.pos.y = 0.0;
        if (k === '2') fppState.pos.y = 3.0;
        return;
    }

    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') { e.preventDefault(); commandHistory.undo(); }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') { e.preventDefault(); commandHistory.redo(); }
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'n') {
        e.preventDefault();
        document.getElementById('cad-new-project-dialog')?.classList.add('show');
    }
    if (e.key.toLowerCase() === 'm' && selectedItem && activeTool === 'SELECT') {
        startMovingSelectedItem();
        return;
    }
    if (e.key.toLowerCase() === 'r') {
        if (activeTool === 'MOVE_OBJECT' && movingObject?.type === 'FURNITURE') {
            freeFurnitureYaw = (freeFurnitureYaw + Math.PI / 4) % (Math.PI * 2);
            setTelemetry(`Obrót mebla o 45° (${Math.round((freeFurnitureYaw * 180 / Math.PI) % 360)}°)`);
            return;
        }
        freeFurnitureYaw = (freeFurnitureYaw + Math.PI / 4) % (Math.PI * 2);
    }
    if (e.key.toLowerCase() === 'v') { enterWallElevation2D(); }
    if (e.key === 'Escape') {
        if (isDraggingWallEndpoint) {
            cancelDraggingWallEndpoint();
            return;
        }
        if (activeTool === 'MOVE_OBJECT') {
            cancelMovingObject();
            return;
        }
        wallChainStart = null;
        clearPreviews();
        closeAllDropdownMenus();
        selectTool('SELECT');
        hideContextMenu();
        document.getElementById('cad-confirm-dialog')?.classList.remove('show');
        document.getElementById('cad-new-project-dialog')?.classList.remove('show');
        document.getElementById('cad-custom-furniture-dialog')?.classList.remove('show');
    }
    if (e.key === 'Delete' || e.key === 'Backspace') {
        requestDeleteSelectedItemWithConfirm();
    }
});

window.addEventListener('keyup', e => {
    const k = e.key.toLowerCase();
    if (k === 'w') fppState.keys.w = false;
    if (k === 's') fppState.keys.s = false;
    if (k === 'a') fppState.keys.a = false;
    if (k === 'd') fppState.keys.d = false;
    if (e.key === 'Shift') fppState.keys.shift = false;
});

window.addEventListener('resize', () => {
    renderer.setSize(window.innerWidth, window.innerHeight);
    perspCamera.aspect = window.innerWidth / window.innerHeight;
    perspCamera.updateProjectionMatrix();
    updateCameraTransform();
});

// Native C# Host Bridge (Save/Load JSON, Load Blueprint PNG/JPG, Import .GLB/.GLTF)
function postToHost(msg) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(msg);
    }
}

document.getElementById('btn-save').addEventListener('click', () => {
    closeAllDropdownMenus();
    postToHost({ type: 'SAVE_PROJECT_JSON', payload: JSON.stringify(projectData, null, 2) });
});
document.getElementById('btn-load').addEventListener('click', () => {
    closeAllDropdownMenus();
    postToHost({ type: 'LOAD_PROJECT_JSON' });
});
function requestOpenBlueprint() {
    closeAllDropdownMenus();
    if (window.chrome && window.chrome.webview) {
        postToHost({ type: 'OPEN_BLUEPRINT_IMAGE' });
    } else {
        const inp = document.getElementById('inp-blueprint-file-fallback');
        if (inp) {
            inp.value = '';
            inp.click();
        }
    }
}

document.getElementById('btn-load-blueprint').addEventListener('click', requestOpenBlueprint);
document.getElementById('btn-load-blueprint-wallmenu')?.addEventListener('click', requestOpenBlueprint);

document.getElementById('inp-blueprint-file-fallback')?.addEventListener('change', e => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = evt => {
        const dataUrl = evt.target.result;
        if (file.name.toLowerCase().endsWith('.pdf')) {
            postToHost({ type: 'PARSE_BLUEPRINT_PDF_DATA', fileName: file.name, dataUrl });
        } else {
            const img = new Image();
            img.onload = () => {
                const fl = getActiveFloor();
                fl.blueprint.dataUrl = dataUrl;
                fl.blueprint.widthPx = img.width;
                fl.blueprint.heightPx = img.height;
                rebuildEntireScene();
                selectTool('CALIBRATE');
                setTelemetry(`Wgrano rzut: ${file.name} (${img.width}×${img.height} px). Kliknij 2 punkty wymiarowe na rzucie, aby skalibrować skalę!`);
            };
            img.src = dataUrl;
        }
    };
    reader.readAsDataURL(file);
});
document.getElementById('btn-import-glb').addEventListener('click', () => {
    closeAllDropdownMenus();
    postToHost({ type: 'IMPORT_GLTF_MODEL' });
});
document.getElementById('btn-import-glb-furnmenu')?.addEventListener('click', () => {
    closeAllDropdownMenus();
    postToHost({ type: 'IMPORT_GLTF_MODEL' });
});

if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', event => {
        const msg = event.data;
        if (!msg || !msg.type) return;

        if (msg.type === 'PROJECT_SAVED') {
            setTelemetry(`Zapisano projekt BIM do pliku: ${msg.filePath}`);
        } else if (msg.type === 'PROJECT_LOADED') {
            projectData = JSON.parse(msg.payload);
            rebuildEntireScene();
            setTelemetry(`Wczytano projekt BIM: ${msg.filePath}`);
        } else if (msg.type === 'BLUEPRINT_IMAGE_LOADED') {
            const img = new Image();
            img.onload = () => {
                const fl = getActiveFloor();
                fl.blueprint.dataUrl = msg.dataUrl;
                fl.blueprint.widthPx = img.width;
                fl.blueprint.heightPx = img.height;
                rebuildEntireScene();
                selectTool('CALIBRATE');
                setTelemetry(`Wgrano rzut: ${msg.fileName} (${img.width}×${img.height} px). Kliknij 2 punkty wymiarowe na rzucie, aby skalibrować skalę!`);
            };
            img.src = msg.dataUrl;
        } else if (msg.type === 'BLUEPRINT_PDF_CAD_PARSED') {
            currentPdfCadData = msg.pdfData;
            currentPdfSvgDataUrl = msg.dataUrl;
            currentPdfFileName = msg.fileName;
            openAutoBimModal();
        } else if (msg.type === 'GLTF_MODEL_LOADED') {
            const binary = Uint8Array.from(atob(msg.base64Data), c => c.charCodeAt(0));
            gltfLoader.parse(binary.buffer, '', gltf => {
                const model = gltf.scene;
                // Normalize Pivot to Bottom-Back-Center (X = Center, Y = MinY, Z = MinZ)
                const box = new THREE.Box3().setFromObject(model);
                const size = new THREE.Vector3();
                const center = new THREE.Vector3();
                box.getSize(size);
                box.getCenter(center);

                model.position.set(-center.x, -box.min.y, -box.min.z);
                model.traverse(node => {
                    if (node.isMesh) { node.castShadow = true; node.receiveShadow = true; }
                });

                const normalizedGroup = new THREE.Group();
                normalizedGroup.add(model);

                const customId = `custom_glb_${Date.now()}`;
                customGltfCache.set(customId, normalizedGroup);
                CATALOG_ITEMS[customId] = {
                    name: msg.fileName,
                    size: { x: Math.max(0.2, size.x), y: Math.max(0.2, size.y), z: Math.max(0.2, size.z) },
                    mountY: 0.0,
                    snapWall: true,
                    hazard: false,
                    hasDoor: false
                };
                activeCatalogId = customId;
                selectTool('PLACE_FURNITURE');
                setTelemetry(`Zaimportowano i znormalizowano model 3D: ${msg.fileName} (${(size.x * 100).toFixed(0)}×${(size.y * 100).toFixed(0)}×${(size.z * 100).toFixed(0)} cm). Kliknij LPM na ścianie/podłodze!`);
            });
        }
    });
}

// ============================================================================
// 11. AUTODESK REVIT / AUTOCAD RIBBON & QAT INTERFACE CONTROLLER
// ============================================================================
function initRibbonUI() {
    document.querySelectorAll('.ribbon-tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const tabId = btn.dataset.tab;
            switchRibbonTab(tabId);
        });
    });

    document.getElementById('btn-toggle-ribbon-min')?.addEventListener('click', () => {
        const ribbonContainer = document.getElementById('cad-ribbon-container');
        if (!ribbonContainer) return;
        const isMin = ribbonContainer.classList.toggle('minimized');
        const minBtn = document.getElementById('btn-toggle-ribbon-min');
        if (minBtn) minBtn.textContent = isMin ? '⌄' : '⌃';
    });

    document.getElementById('btn-toggle-cam-qat')?.addEventListener('click', () => {
        document.getElementById('btn-toggle-cam')?.click();
    });

    document.getElementById('btn-save-ribbon')?.addEventListener('click', () => {
        document.getElementById('btn-save')?.click();
    });

    document.getElementById('btn-load-ribbon')?.addEventListener('click', () => {
        document.getElementById('btn-load')?.click();
    });
}

function switchRibbonTab(tabId) {
    document.querySelectorAll('.ribbon-tab-btn').forEach(btn => {
        const isActive = btn.dataset.tab === tabId;
        btn.classList.toggle('active', isActive);
        btn.setAttribute('aria-selected', isActive ? 'true' : 'false');
    });
    document.querySelectorAll('.ribbon-tab-panel').forEach(panel => {
        panel.classList.toggle('active', panel.id === tabId);
    });
    const ribbonContainer = document.getElementById('cad-ribbon-container');
    if (ribbonContainer?.classList.contains('minimized')) {
        ribbonContainer.classList.remove('minimized');
        const minBtn = document.getElementById('btn-toggle-ribbon-min');
        if (minBtn) minBtn.textContent = '⌃';
    }
}

// ============================================================================
// 12. ADVANCED CAD ENHANCEMENTS: CONTEXT MENU, MODALS & PROJECT WIZARD
// ============================================================================
function initContextMenuAndModals() {
    // 1. Right-Click Context Menu Actions & Toolbar Actions
    document.getElementById('ctx-action-move')?.addEventListener('click', () => {
        hideContextMenu();
        startMovingSelectedItem();
    });

    document.getElementById('btn-move-selected')?.addEventListener('click', () => {
        startMovingSelectedItem();
    });

    document.getElementById('ctx-action-delete')?.addEventListener('click', () => {
        hideContextMenu();
        requestDeleteSelectedItemWithConfirm();
    });

    document.getElementById('ctx-action-rotate')?.addEventListener('click', () => {
        if (selectedItem?.type === 'FURNITURE') {
            executeWithUndo('Obrót mebla o 45°', () => {
                const furn = getActiveFloor().furnitures?.find(x => x.id === selectedItem.id);
                if (furn) {
                    furn.rotY = ((furn.rotY || 0) + Math.PI / 4) % (Math.PI * 2);
                }
            });
            rebuildEntireScene();
            updateActiveToolSummary();
            const furn = getActiveFloor().furnitures?.find(x => x.id === selectedItem.id);
            const rotEl = document.getElementById('inp-furn-rot');
            if (rotEl && furn) rotEl.value = String(Math.round(((furn.rotY || 0) * 180 / Math.PI) % 360));
        }
        hideContextMenu();
    });

    document.getElementById('ctx-action-props')?.addEventListener('click', () => {
        hideContextMenu();
        if (selectedItem?.type === 'WALL') {
            switchRibbonTab('tab-architecture');
            const inpL = document.getElementById('inp-wall-length');
            if (inpL) {
                setTimeout(() => { inpL.focus(); inpL.select(); }, 60);
            }
        } else if (selectedItem?.type === 'OPENING') {
            switchRibbonTab('tab-architecture');
        } else if (selectedItem?.type === 'FURNITURE') {
            switchRibbonTab('tab-interior');
        }
        setTelemetry(`Właściwości obiektu aktywne w pasku opcji pod wstęgą (wpisz długość ściany lub przeciągnij uchwyty).`);
    });

    document.getElementById('ctx-action-deselect')?.addEventListener('click', () => {
        selectedItem = null;
        hideContextMenu();
        rebuildEntireScene();
        updateActiveToolSummary();
    });

    // Dismiss context menu on click outside
    window.addEventListener('click', e => {
        if (!e.target.closest('#cad-context-menu')) {
            hideContextMenu();
        }
    });

    // 2. Delete Confirmation Modal Dialog
    const confirmDialog = document.getElementById('cad-confirm-dialog');
    document.getElementById('confirm-dialog-ok')?.addEventListener('click', () => {
        deleteSelectedItem();
        confirmDialog?.classList.remove('show');
    });

    document.getElementById('confirm-dialog-cancel')?.addEventListener('click', () => {
        confirmDialog?.classList.remove('show');
    });

    document.getElementById('confirm-dialog-close-btn')?.addEventListener('click', () => {
        confirmDialog?.classList.remove('show');
    });

    // 3. New Project Wizard Dialog
    const newProjDialog = document.getElementById('cad-new-project-dialog');
    let selectedNewProjTemplate = 'blank';

    const openNewProjectModal = () => {
        hideContextMenu();
        newProjDialog?.classList.add('show');
    };

    document.getElementById('btn-new-project')?.addEventListener('click', openNewProjectModal);
    document.getElementById('btn-new-ribbon')?.addEventListener('click', openNewProjectModal);

    document.getElementById('btn-close-new-project-modal')?.addEventListener('click', () => {
        newProjDialog?.classList.remove('show');
    });
    document.getElementById('btn-cancel-new-project')?.addEventListener('click', () => {
        newProjDialog?.classList.remove('show');
    });

    document.querySelectorAll('#cad-new-project-dialog .template-card').forEach(card => {
        card.addEventListener('click', () => {
            document.querySelectorAll('#cad-new-project-dialog .template-card').forEach(c => c.classList.remove('active'));
            card.classList.add('active');
            selectedNewProjTemplate = card.dataset.template || 'blank';
        });
    });

    document.getElementById('btn-submit-new-project')?.addEventListener('click', () => {
        const name = document.getElementById('inp-new-proj-name')?.value.trim() || 'Nowy_Projekt_1';
        const wallH = Math.max(2.0, Math.min(4.5, parseFloat(document.getElementById('inp-new-proj-height')?.value) || 2.75));
        const numFloors = parseInt(document.getElementById('sel-new-proj-floors')?.value, 10) || 1;

        createNewProject(selectedNewProjTemplate, name, wallH, numFloors);
        newProjDialog?.classList.remove('show');
    });

    // 4. Custom Furniture Creator Dialog
    const customFurnDialog = document.getElementById('cad-custom-furniture-dialog');
    
    document.getElementById('btn-create-custom-furniture')?.addEventListener('click', () => {
        hideContextMenu();
        customFurnDialog?.classList.add('show');
    });

    document.getElementById('btn-close-custom-furn-modal')?.addEventListener('click', () => {
        customFurnDialog?.classList.remove('show');
    });
    document.getElementById('btn-cancel-custom-furn')?.addEventListener('click', () => {
        customFurnDialog?.classList.remove('show');
    });

    document.getElementById('btn-submit-custom-furn')?.addEventListener('click', () => {
        const name = document.getElementById('inp-custom-furn-name')?.value.trim() || 'Szafka_Custom';
        const type = document.getElementById('sel-custom-furn-type')?.value || 'base';
        const w = Math.max(0.20, Math.min(6.00, parseFloat(document.getElementById('inp-custom-furn-w')?.value) || 1.20));
        const d = Math.max(0.20, Math.min(3.00, parseFloat(document.getElementById('inp-custom-furn-d')?.value) || 0.60));
        const h = Math.max(0.20, Math.min(3.20, parseFloat(document.getElementById('inp-custom-furn-h')?.value) || 0.85));
        const mat = document.getElementById('sel-custom-furn-mat')?.value || 'wood';

        const customId = `custom_furn_${Date.now()}`;
        let mountY = 0.0;
        let snapWall = true;
        if (type === 'wall') mountY = 1.45;
        if (type === 'free') snapWall = false;

        CATALOG_ITEMS[customId] = {
            name: name,
            size: { x: w, y: h, z: d },
            mountY: mountY,
            customMat: mat,
            snapWall: snapWall,
            hazard: false,
            hasDoor: false
        };

        activeCatalogId = customId;
        document.querySelectorAll('.catalog-btn').forEach(b => b.classList.remove('active'));
        selectTool('PLACE_FURNITURE');
        customFurnDialog?.classList.remove('show');
        setTelemetry(`Zaprojektowano mebel autorski: "${name}" (${(w * 100).toFixed(0)}×${(d * 100).toFixed(0)}×${(h * 100).toFixed(0)} cm). Kliknij na rzucie lub ścianie, aby go wstawić!`);
    });
}

function createNewProject(template, projectName, wallHeight, numFloors) {
    commandHistory.clear();
    selectedItem = null;

    let floors = [];
    const floor0 = {
        id: 'floor_0',
        name: 'Parter',
        levelIndex: 0,
        elevationY: 0.0,
        defaultHeight: wallHeight,
        isAttic: false,
        blueprint: { dataUrl: null, metersPerPixel: 0.01, widthPx: 1000, heightPx: 800, visible: true },
        walls: [],
        furnitures: [],
        ceilingLights: [],
        mepLinks: []
    };

    if (template === 'starter') {
        const starter = createStarterProject();
        floors = numFloors === 1 ? [starter.floors[0]] : starter.floors;
        floors.forEach((fl, idx) => {
            fl.defaultHeight = wallHeight;
            fl.elevationY = idx * wallHeight;
            fl.walls.forEach(w => { w.height = wallHeight; });
        });
    } else if (template === 'perimeter') {
        floor0.walls = [
            { id: 'w_p1', start: { x: -4.0, z: -5.0 }, end: { x: 4.0, z: -5.0 }, thickness: 0.24, height: wallHeight, clipRoof: false, openings: [], mep: [] },
            { id: 'w_p2', start: { x: 4.0, z: -5.0 }, end: { x: 4.0, z: 5.0 }, thickness: 0.24, height: wallHeight, clipRoof: false, openings: [], mep: [] },
            { id: 'w_p3', start: { x: 4.0, z: 5.0 }, end: { x: -4.0, z: 5.0 }, thickness: 0.24, height: wallHeight, clipRoof: false, openings: [], mep: [] },
            { id: 'w_p4', start: { x: -4.0, z: 5.0 }, end: { x: -4.0, z: -5.0 }, thickness: 0.24, height: wallHeight, clipRoof: false, openings: [], mep: [] }
        ];
        floor0.ceilingLights = [
            { id: 'cl_1', category: 'CEILING_LIGHT', worldPos: { x: 0, y: wallHeight - 0.05, z: 0 }, normal: { x: 0, y: -1, z: 0 } }
        ];
        floors = [floor0];
        if (numFloors >= 2) {
            const floor1 = {
                id: 'floor_1',
                name: 'Piętro 1',
                levelIndex: 1,
                elevationY: wallHeight,
                defaultHeight: wallHeight,
                isAttic: false,
                blueprint: { dataUrl: null, metersPerPixel: 0.01, widthPx: 1000, heightPx: 800, visible: true },
                walls: [
                    { id: 'w_p1_f1', start: { x: -4.0, z: -5.0 }, end: { x: 4.0, z: -5.0 }, thickness: 0.24, height: wallHeight, clipRoof: false, openings: [], mep: [] },
                    { id: 'w_p2_f1', start: { x: 4.0, z: -5.0 }, end: { x: 4.0, z: 5.0 }, thickness: 0.24, height: wallHeight, clipRoof: false, openings: [], mep: [] },
                    { id: 'w_p3_f1', start: { x: 4.0, z: 5.0 }, end: { x: -4.0, z: 5.0 }, thickness: 0.24, height: wallHeight, clipRoof: false, openings: [], mep: [] },
                    { id: 'w_p4_f1', start: { x: -4.0, z: 5.0 }, end: { x: -4.0, z: -5.0 }, thickness: 0.24, height: wallHeight, clipRoof: false, openings: [], mep: [] }
                ],
                furnitures: [],
                ceilingLights: [],
                mepLinks: []
            };
            floors.push(floor1);
        }
    } else { // blank
        floors = [floor0];
        if (numFloors >= 2) {
            floors.push({
                id: 'floor_1',
                name: 'Piętro 1',
                levelIndex: 1,
                elevationY: wallHeight,
                defaultHeight: wallHeight,
                isAttic: false,
                blueprint: { dataUrl: null, metersPerPixel: 0.01, widthPx: 1000, heightPx: 800, visible: true },
                walls: [],
                furnitures: [],
                ceilingLights: [],
                mepLinks: []
            });
        }
    }

    projectData = {
        schemaVersion: '5.0.0',
        name: projectName,
        timeOfDayHours: 14.5,
        activeFloorIndex: 0,
        showRoofMesh: false,
        floors: floors
    };

    resetCameraCenterFit();
    rebuildEntireScene();
    selectTool('SELECT');
    syncAllFloorsButtonUI();
    setTelemetry(`Utworzono nowy projekt BIM: "${projectName}" (${floors.length} kondygnacje, wys. ${wallHeight.toFixed(2)} m).`);
}

// ============================================================================
// 13. AUTO-BIM: UNIVERSAL CAD VECTOR PDF IMPORTER (WALLS & OPENINGS)
// ============================================================================
let currentPdfCadData = null;
let currentPdfSvgDataUrl = null;
let currentPdfFileName = null;
let currentGeneratedBimWalls = [];
let currentGeneratedBimColumns = [];
let currentBimScaleFactor = null;
let currentBimBaseScale100 = 1 / 236.2205;
let bimPreviewState = {
    zoom: 1.0,
    panX: 0,
    panY: 0,
    isDragging: false,
    dragStartX: 0,
    dragStartY: 0,
    initialPanX: 0,
    initialPanY: 0,
    fitMode: 'building'
};

function recomputeBuildingBoundsFromWallLayer() {
    if (!currentPdfCadData) return;
    const selWallTag = document.getElementById('sel-bim-wall-layer')?.value;
    const layers = currentPdfCadData.layers || currentPdfCadData.Layers || [];
    const wallLayer = layers.find(l => (l.tag || l.Tag) === selWallTag);
    if (!wallLayer) return;

    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    (wallLayer.segments || wallLayer.Segments || []).forEach(s => {
        const x1 = s.x1 !== undefined ? s.x1 : s.X1;
        const y1 = s.y1 !== undefined ? s.y1 : s.Y1;
        const x2 = s.x2 !== undefined ? s.x2 : s.X2;
        const y2 = s.y2 !== undefined ? s.y2 : s.Y2;
        if (x1 < minX) minX = x1;
        if (x2 > maxX) maxX = x2;
        if (y1 < minY) minY = y1;
        if (y2 > maxY) maxY = y2;
    });

    if (minX < maxX && minY < maxY) {
        currentPdfCadData.buildingMinX = minX;
        currentPdfCadData.buildingMaxX = maxX;
        currentPdfCadData.buildingMinY = minY;
        currentPdfCadData.buildingMaxY = maxY;
        currentPdfCadData.buildingCenterX = (minX + maxX) * 0.5;
        currentPdfCadData.buildingCenterY = (minY + maxY) * 0.5;
    }
}

function refreshBimDimensionsPreview(updateInput = true) {
    if (!currentPdfCadData) return;
    const bldgMinX = currentPdfCadData.buildingMinX ?? currentPdfCadData.BuildingMinX ?? (currentPdfCadData.boundsMinX || 0);
    const bldgMaxX = currentPdfCadData.buildingMaxX ?? currentPdfCadData.BuildingMaxX ?? (currentPdfCadData.boundsMaxX || 1000);
    const bldgMinY = currentPdfCadData.buildingMinY ?? currentPdfCadData.BuildingMinY ?? (currentPdfCadData.boundsMinY || 0);
    const bldgMaxY = currentPdfCadData.buildingMaxY ?? currentPdfCadData.BuildingMaxY ?? (currentPdfCadData.boundsMaxY || 1000);

    const bSpanX = Math.max(1, bldgMaxX - bldgMinX);
    const bSpanY = Math.max(1, bldgMaxY - bldgMinY);
    const sf = currentBimScaleFactor || (1 / 236.2205);

    const widthM = bSpanX * sf;
    const depthM = bSpanY * sf;

    const lblDims = document.getElementById('lbl-bim-dimensions-preview');
    if (lblDims) {
        lblDims.textContent = `${widthM.toFixed(2)} m × ${depthM.toFixed(2)} m`;
    }
    if (updateInput) {
        const inpWidth = document.getElementById('inp-bim-calibrate-width');
        if (inpWidth) inpWidth.value = widthM.toFixed(2);
    }
}

function getBimCanvasTransform() {
    if (!currentPdfCadData) return null;
    const canvas = document.getElementById('canvas-bim-preview');
    if (!canvas) return null;
    const cw = canvas.width;
    const ch = canvas.height;
    if (cw <= 0 || ch <= 0) return null;

    const bxMin = currentPdfCadData.boundsMinX ?? currentPdfCadData.BoundsMinX ?? 0;
    const bxMax = currentPdfCadData.boundsMaxX ?? currentPdfCadData.BoundsMaxX ?? 1000;
    const byMin = currentPdfCadData.boundsMinY ?? currentPdfCadData.BoundsMinY ?? 0;
    const byMax = currentPdfCadData.boundsMaxY ?? currentPdfCadData.BoundsMaxY ?? 1000;

    const bldgMinX = currentPdfCadData.buildingMinX ?? currentPdfCadData.BuildingMinX ?? bxMin;
    const bldgMaxX = currentPdfCadData.buildingMaxX ?? currentPdfCadData.BuildingMaxX ?? bxMax;
    const bldgMinY = currentPdfCadData.buildingMinY ?? currentPdfCadData.BuildingMinY ?? byMin;
    const bldgMaxY = currentPdfCadData.buildingMaxY ?? currentPdfCadData.BuildingMaxY ?? byMax;

    let spanX, spanY, cx0, cy0;
    if (bimPreviewState.fitMode === 'all') {
        spanX = Math.max(10, bxMax - bxMin);
        spanY = Math.max(10, byMax - byMin);
        cx0 = (bxMin + bxMax) * 0.5;
        cy0 = (byMin + byMax) * 0.5;
    } else {
        spanX = Math.max(10, bldgMaxX - bldgMinX);
        spanY = Math.max(10, bldgMaxY - bldgMinY);
        cx0 = (bldgMinX + bldgMaxX) * 0.5;
        cy0 = (bldgMinY + bldgMaxY) * 0.5;
    }

    const pad = 36;
    const baseScale = Math.min((cw - pad * 2) / spanX, (ch - pad * 2) / spanY);

    return { cw, ch, cx0, cy0, baseScale, bldgMinX, bldgMaxX, bldgMinY, bldgMaxY, bxMin, bxMax, byMin, byMax };
}

function initAutoBimModal() {
    const modal = document.getElementById('cad-auto-bim-dialog');
    if (!modal) return;

    document.getElementById('btn-close-auto-bim-modal')?.addEventListener('click', () => {
        modal.classList.remove('show');
    });

    document.getElementById('btn-cancel-auto-bim')?.addEventListener('click', () => {
        modal.classList.remove('show');
    });

    // Dynamic preview refresh on any setting change
    document.getElementById('sel-bim-wall-layer')?.addEventListener('change', () => {
        recomputeBuildingBoundsFromWallLayer();
        refreshBimDimensionsPreview();
        updateBimPreview();
    });
    document.getElementById('sel-bim-win-layer')?.addEventListener('change', updateBimPreview);
    document.getElementById('sel-bim-door-layer')?.addEventListener('change', updateBimPreview);
    document.getElementById('inp-bim-wall-height')?.addEventListener('input', updateBimPreview);
    document.getElementById('sel-bim-wall-thickness')?.addEventListener('change', updateBimPreview);

    // Scale selector dropdown
    const selScale = document.getElementById('sel-bim-scale');
    selScale?.addEventListener('change', () => {
        const val = selScale.value;
        if (val !== 'custom') {
            const parts = val.split(':');
            const ratio = parseFloat(parts[1]) || 100;
            currentBimScaleFactor = currentBimBaseScale100 * (ratio / 100);
            refreshBimDimensionsPreview(true);
            updateBimPreview();
        }
    });

    // Known width calibration input
    const inpWidth = document.getElementById('inp-bim-calibrate-width');
    inpWidth?.addEventListener('input', () => {
        const targetW = parseFloat(inpWidth.value);
        if (targetW > 0.5 && targetW < 500 && currentPdfCadData) {
            const bldgMinX = currentPdfCadData.buildingMinX ?? currentPdfCadData.BuildingMinX ?? (currentPdfCadData.boundsMinX || 0);
            const bldgMaxX = currentPdfCadData.buildingMaxX ?? currentPdfCadData.BuildingMaxX ?? (currentPdfCadData.boundsMaxX || 1000);
            const bSpanX = Math.max(1, bldgMaxX - bldgMinX);
            currentBimScaleFactor = targetW / bSpanX;
            if (selScale) selScale.value = 'custom';
            refreshBimDimensionsPreview(false);
            updateBimPreview();
        }
    });

    // Zoom & pan buttons
    document.getElementById('btn-bim-zoom-in')?.addEventListener('click', () => {
        bimPreviewState.zoom = Math.min(50, bimPreviewState.zoom * 1.25);
        updateBimPreview();
    });
    document.getElementById('btn-bim-zoom-out')?.addEventListener('click', () => {
        bimPreviewState.zoom = Math.max(0.08, bimPreviewState.zoom / 1.25);
        updateBimPreview();
    });
    document.getElementById('btn-bim-fit-building')?.addEventListener('click', () => {
        bimPreviewState.fitMode = 'building';
        bimPreviewState.zoom = 1.0;
        bimPreviewState.panX = 0;
        bimPreviewState.panY = 0;
        updateBimPreview();
    });
    document.getElementById('btn-bim-fit-all')?.addEventListener('click', () => {
        bimPreviewState.fitMode = 'all';
        bimPreviewState.zoom = 1.0;
        bimPreviewState.panX = 0;
        bimPreviewState.panY = 0;
        updateBimPreview();
    });

    // Canvas Mouse Interaction (Pan & Wheel Zoom)
    const canvas = document.getElementById('canvas-bim-preview');
    const container = document.getElementById('bim-preview-container');
    if (canvas) {
        canvas.addEventListener('wheel', (e) => {
            e.preventDefault();
            const rect = canvas.getBoundingClientRect();
            const mx = e.clientX - rect.left;
            const my = e.clientY - rect.top;

            const t = getBimCanvasTransform();
            if (!t) return;

            // CAD coordinate under mouse cursor before zoom
            const cadX = t.cx0 + (mx - t.cw * 0.5 - bimPreviewState.panX) / (t.baseScale * bimPreviewState.zoom);
            const cadY = t.cy0 - (my - t.ch * 0.5 - bimPreviewState.panY) / (t.baseScale * bimPreviewState.zoom);

            const zoomMult = e.deltaY < 0 ? 1.20 : 0.8333;
            const newZoom = Math.min(50, Math.max(0.08, bimPreviewState.zoom * zoomMult));

            // Adjust pan so (cadX, cadY) stays under cursor
            bimPreviewState.panX = mx - t.cw * 0.5 - (cadX - t.cx0) * t.baseScale * newZoom;
            bimPreviewState.panY = my - t.ch * 0.5 + (cadY - t.cy0) * t.baseScale * newZoom;
            bimPreviewState.zoom = newZoom;

            updateBimPreview();
        }, { passive: false });

        canvas.addEventListener('mousedown', (e) => {
            if (e.button === 0 || e.button === 1) { // Left or Middle click
                bimPreviewState.isDragging = true;
                bimPreviewState.dragStartX = e.clientX;
                bimPreviewState.dragStartY = e.clientY;
                bimPreviewState.initialPanX = bimPreviewState.panX;
                bimPreviewState.initialPanY = bimPreviewState.panY;
                canvas.style.cursor = 'grabbing';
            }
        });

        canvas.addEventListener('mousemove', (e) => {
            const rect = canvas.getBoundingClientRect();
            const mx = e.clientX - rect.left;
            const my = e.clientY - rect.top;

            if (bimPreviewState.isDragging) {
                bimPreviewState.panX = bimPreviewState.initialPanX + (e.clientX - bimPreviewState.dragStartX);
                bimPreviewState.panY = bimPreviewState.initialPanY + (e.clientY - bimPreviewState.dragStartY);
                updateBimPreview();
            }

            const t = getBimCanvasTransform();
            if (t) {
                const cadX = t.cx0 + (mx - t.cw * 0.5 - bimPreviewState.panX) / (t.baseScale * bimPreviewState.zoom);
                const cadY = t.cy0 - (my - t.ch * 0.5 - bimPreviewState.panY) / (t.baseScale * bimPreviewState.zoom);
                const sf = currentBimScaleFactor || (1 / 236.2205);
                const relX = (cadX - t.bldgMinX) * sf;
                const relY = (cadY - t.bldgMinY) * sf;
                const lblCursor = document.getElementById('lbl-bim-cursor-pos');
                if (lblCursor) {
                    lblCursor.textContent = `X: ${relX.toFixed(2)} m, Y: ${relY.toFixed(2)} m`;
                }
            }
        });

        window.addEventListener('mouseup', () => {
            if (bimPreviewState.isDragging) {
                bimPreviewState.isDragging = false;
                if (canvas) canvas.style.cursor = 'grab';
            }
        });
    }

    if (container && !window._bimResizeObserver) {
        window._bimResizeObserver = new ResizeObserver(() => {
            if (modal.classList.contains('show') && canvas) {
                canvas.width = container.clientWidth;
                canvas.height = container.clientHeight;
                updateBimPreview();
            }
        });
        window._bimResizeObserver.observe(container);
    }

    // Confirmation: Generate 3D BIM Model
    document.getElementById('btn-submit-auto-bim')?.addEventListener('click', () => {
        if (!currentPdfCadData || currentGeneratedBimWalls.length === 0) {
            setTelemetry('Brak wykrytych ścian do wygenerowania!', true);
            return;
        }

        const wallHeight = Math.max(1.8, Math.min(6.0, parseFloat(document.getElementById('inp-bim-wall-height')?.value) || 2.75));
        const fl = getActiveFloor();
        const sFactor = currentBimScaleFactor || currentPdfCadData.scaleFactor || currentPdfCadData.ScaleFactor || (1 / 236.2205);

        // 1. Attach SVG blueprint as floor underlay with accurate alignment
        if (currentPdfSvgDataUrl) {
            const bxMin = currentPdfCadData.boundsMinX ?? currentPdfCadData.BoundsMinX ?? 0;
            const bxMax = currentPdfCadData.boundsMaxX ?? currentPdfCadData.BoundsMaxX ?? 1000;
            const byMin = currentPdfCadData.boundsMinY ?? currentPdfCadData.BoundsMinY ?? 0;
            const byMax = currentPdfCadData.boundsMaxY ?? currentPdfCadData.BoundsMaxY ?? 1000;

            const w = Math.max(100, bxMax - bxMin);
            const h = Math.max(100, byMax - byMin);
            const pad = Math.max(w, h) * 0.05;
            const vw = w + pad * 2;
            const vh = h + pad * 2;

            const cxSheet = (bxMin + bxMax) * 0.5;
            const cySheet = (byMin + byMax) * 0.5;
            const cxBldg = currentPdfCadData.buildingCenterX ?? currentPdfCadData.BuildingCenterX ?? cxSheet;
            const cyBldg = currentPdfCadData.buildingCenterY ?? currentPdfCadData.BuildingCenterY ?? cySheet;

            fl.blueprint.dataUrl = currentPdfSvgDataUrl;
            fl.blueprint.widthPx = vw;
            fl.blueprint.heightPx = vh;
            fl.blueprint.metersPerPixel = sFactor;
            fl.blueprint.offsetX = (cxSheet - cxBldg) * sFactor;
            fl.blueprint.offsetZ = -(cySheet - cyBldg) * sFactor;
            fl.blueprint.visible = true;
        }

        // 2. Generate Walls & Openings with Undo/Redo
        executeWithUndo(`Import Rzutu PDF Auto-BIM (${currentPdfFileName || 'PDF'})`, () => {
            fl.defaultHeight = wallHeight;
            fl.walls = currentGeneratedBimWalls.map(w => ({
                id: nextId('w'),
                start: { x: Math.round(w.x1 * 1000) / 1000, z: Math.round(w.z1 * 1000) / 1000 },
                end: { x: Math.round(w.x2 * 1000) / 1000, z: Math.round(w.z2 * 1000) / 1000 },
                thickness: w.thickness || 0.24,
                height: wallHeight,
                clipRoof: false,
                openings: (w.openings || []).map(op => ({
                    id: nextId('op'),
                    type: op.type || 'WINDOW',
                    dist: Math.round(op.dist * 100) / 100,
                    width: Math.round(op.width * 100) / 100,
                    height: Math.round((op.height || (op.type === 'GARAGE_DOOR' ? 2.15 : op.type === 'DOOR' ? 2.05 : 1.40)) * 100) / 100,
                    sill: op.sill !== undefined ? op.sill : (op.type === 'GARAGE_DOOR' || op.type === 'DOOR' ? 0.0 : 0.85),
                    color: op.color || 'anthracite',
                    flip: false
                })),
                mep: []
            }));
            fl.columns = (currentGeneratedBimColumns || []).map(col => ({
                id: nextId('col'),
                pos: { x: Math.round(col.pos.x * 1000) / 1000, z: Math.round(col.pos.z * 1000) / 1000 },
                width: col.width || 0.30,
                depth: col.depth || col.width || 0.30,
                height: wallHeight,
                round: !!col.round
            }));
        });

        modal.classList.remove('show');
        resetCameraCenterFit();
        rebuildEntireScene();
        selectTool('SELECT');

        let totalOps = 0;
        fl.walls.forEach(w => totalOps += (w.openings || []).length);
        const colCnt = (fl.columns || []).length;
        setTelemetry(`✅ Wygenerowano model 3D BIM ze skalą 1:${Math.round(1 / (sFactor * currentBimBaseScale100 * 100)) || 100}: ${fl.walls.length} ścian, ${colCnt} słupów, ${totalOps} otworów stolarki! (Wysokość: ${wallHeight.toFixed(2)} m)`);
    });
}

function openAutoBimModal() {
    if (!currentPdfCadData) return;
    const modal = document.getElementById('cad-auto-bim-dialog');
    if (!modal) return;

    const fnEl = document.getElementById('lbl-bim-filename');
    if (fnEl) fnEl.textContent = currentPdfFileName ? `[${currentPdfFileName}]` : '';

    const selWall = document.getElementById('sel-bim-wall-layer');
    const selWin = document.getElementById('sel-bim-win-layer');
    const selDoor = document.getElementById('sel-bim-door-layer');

    const recWall = currentPdfCadData.recommendedWallLayer || currentPdfCadData.RecommendedWallLayer || '';
    const recWin = currentPdfCadData.recommendedWindowLayer || currentPdfCadData.RecommendedWindowLayer || '';
    const recDoor = currentPdfCadData.recommendedDoorLayer || currentPdfCadData.RecommendedDoorLayer || '';

    const layers = currentPdfCadData.layers || currentPdfCadData.Layers || [];

    const populateSelect = (selectEl, recommendedTag) => {
        if (!selectEl) return;
        selectEl.innerHTML = '';
        layers.forEach(l => {
            const tag = l.tag || l.Tag || '';
            const name = l.name || l.Name || tag;
            const segCnt = l.segmentCount || l.SegmentCount || 0;
            const opt = document.createElement('option');
            opt.value = tag;
            const isRec = tag === recommendedTag ? ' ★ (Zalecana)' : '';
            opt.textContent = `${name} [${segCnt} linii]${isRec}`;
            selectEl.appendChild(opt);
        });
        if (recommendedTag) selectEl.value = recommendedTag;
    };

    populateSelect(selWall, recWall);
    populateSelect(selWin, recWin);
    populateSelect(selDoor, recDoor);

    // Initialize Scale & Dimensions
    const detScale = currentPdfCadData.detectedScale || currentPdfCadData.DetectedScale || '1:100';
    const detRatio = currentPdfCadData.detectedScaleRatio || currentPdfCadData.DetectedScaleRatio || 100;
    const sf = currentPdfCadData.scaleFactor || currentPdfCadData.ScaleFactor || (1 / 236.2205);
    currentBimScaleFactor = sf;
    currentBimBaseScale100 = sf / (detRatio / 100);

    const selScale = document.getElementById('sel-bim-scale');
    if (selScale) {
        let matched = false;
        for (let i = 0; i < selScale.options.length; i++) {
            if (selScale.options[i].value === detScale) {
                selScale.selectedIndex = i;
                matched = true;
                break;
            }
        }
        if (!matched) selScale.value = '1:100';
    }

    refreshBimDimensionsPreview();

    // Reset Zoom / Pan state to Building Fit
    bimPreviewState.zoom = 1.0;
    bimPreviewState.panX = 0;
    bimPreviewState.panY = 0;
    bimPreviewState.fitMode = 'building';

    // Show modal & resize preview canvas
    modal.classList.add('show');
    const container = document.getElementById('bim-preview-container');
    const canvas = document.getElementById('canvas-bim-preview');
    if (container && canvas) {
        canvas.width = container.clientWidth || 800;
        canvas.height = container.clientHeight || 550;
    }

    updateBimPreview();
}

function updateBimPreview() {
    if (!currentPdfCadData) return;
    const canvas = document.getElementById('canvas-bim-preview');
    if (!canvas) return;
    const container = document.getElementById('bim-preview-container');
    if (container && (canvas.width !== container.clientWidth || canvas.height !== container.clientHeight)) {
        canvas.width = container.clientWidth || 800;
        canvas.height = container.clientHeight || 550;
    }

    const t = getBimCanvasTransform();
    if (!t) return;
    const { cw, ch, cx0, cy0, baseScale } = t;

    const ctx = canvas.getContext('2d');
    const zoom = bimPreviewState.zoom;
    const panX = bimPreviewState.panX;
    const panY = bimPreviewState.panY;

    // Update zoom readout badge
    const lblZoom = document.getElementById('lbl-bim-zoom-pct');
    if (lblZoom) lblZoom.textContent = `${Math.round(zoom * 100)}%`;

    const toCanvasX = x => cw * 0.5 + panX + (x - cx0) * baseScale * zoom;
    const toCanvasY = y => ch * 0.5 + panY - (y - cy0) * baseScale * zoom;

    const recWall = currentPdfCadData.recommendedWallLayer || currentPdfCadData.RecommendedWallLayer || '';
    const recWin = currentPdfCadData.recommendedWindowLayer || currentPdfCadData.RecommendedWindowLayer || '';
    const recDoor = currentPdfCadData.recommendedDoorLayer || currentPdfCadData.RecommendedDoorLayer || '';

    const selWallTag = document.getElementById('sel-bim-wall-layer')?.value || recWall;
    const selWinTag = document.getElementById('sel-bim-win-layer')?.value || recWin;
    const selDoorTag = document.getElementById('sel-bim-door-layer')?.value || recDoor;
    const wallHeight = Math.max(1.8, Math.min(6.0, parseFloat(document.getElementById('inp-bim-wall-height')?.value) || 2.75));
    const forcedThick = document.getElementById('sel-bim-wall-thickness')?.value || 'auto';

    const layers = currentPdfCadData.layers || currentPdfCadData.Layers || [];
    const wallLayer = layers.find(l => (l.tag || l.Tag) === selWallTag);
    const winLayer = layers.find(l => (l.tag || l.Tag) === selWinTag);
    const doorLayer = layers.find(l => (l.tag || l.Tag) === selDoorTag);

    // Clear Canvas with CAD dark background
    ctx.fillStyle = '#0f1319';
    ctx.fillRect(0, 0, cw, ch);

    // Subtle CAD Grid
    const gridCadStep = 100;
    const minCadX = cx0 - (cw * 0.5 + panX) / (baseScale * zoom);
    const maxCadX = cx0 + (cw * 0.5 - panX) / (baseScale * zoom);
    const minCadY = cy0 - (ch * 0.5 - panY) / (baseScale * zoom);
    const maxCadY = cy0 + (ch * 0.5 + panY) / (baseScale * zoom);

    ctx.strokeStyle = '#181e29';
    ctx.lineWidth = 1.0;
    const firstGx = Math.floor(minCadX / gridCadStep) * gridCadStep;
    const lastGx = Math.ceil(maxCadX / gridCadStep) * gridCadStep;
    ctx.beginPath();
    for (let gx = firstGx; gx <= lastGx; gx += gridCadStep) {
        const sx = toCanvasX(gx);
        ctx.moveTo(sx, 0);
        ctx.lineTo(sx, ch);
    }
    const firstGy = Math.floor(minCadY / gridCadStep) * gridCadStep;
    const lastGy = Math.ceil(maxCadY / gridCadStep) * gridCadStep;
    for (let gy = firstGy; gy <= lastGy; gy += gridCadStep) {
        const sy = toCanvasY(gy);
        ctx.moveTo(0, sy);
        ctx.lineTo(cw, sy);
    }
    ctx.stroke();

    // 1. Draw Background CAD Layers in Muted Charcoal
    ctx.lineWidth = 0.8;
    ctx.strokeStyle = '#273142';
    layers.forEach(l => {
        const tag = l.tag || l.Tag;
        if (tag === selWallTag || tag === selWinTag || tag === selDoorTag) return;
        const segs = l.segments || l.Segments || [];
        ctx.beginPath();
        segs.forEach(s => {
            const x1 = s.x1 !== undefined ? s.x1 : s.X1;
            const y1 = s.y1 !== undefined ? s.y1 : s.Y1;
            const x2 = s.x2 !== undefined ? s.x2 : s.X2;
            const y2 = s.y2 !== undefined ? s.y2 : s.Y2;
            ctx.moveTo(toCanvasX(x1), toCanvasY(y1));
            ctx.lineTo(toCanvasX(x2), toCanvasY(y2));
        });
        ctx.stroke();
    });

    // 2. Draw Window Layer in Cyan
    if (winLayer) {
        ctx.strokeStyle = '#22b8cf';
        ctx.lineWidth = 1.6;
        ctx.beginPath();
        (winLayer.segments || winLayer.Segments || []).forEach(s => {
            const x1 = s.x1 !== undefined ? s.x1 : s.X1;
            const y1 = s.y1 !== undefined ? s.y1 : s.Y1;
            const x2 = s.x2 !== undefined ? s.x2 : s.X2;
            const y2 = s.y2 !== undefined ? s.y2 : s.Y2;
            ctx.moveTo(toCanvasX(x1), toCanvasY(y1));
            ctx.lineTo(toCanvasX(x2), toCanvasY(y2));
        });
        ctx.stroke();
    }

    // 3. Draw Door Layer in Orange
    if (doorLayer) {
        ctx.strokeStyle = '#fd7e14';
        ctx.lineWidth = 1.6;
        ctx.beginPath();
        (doorLayer.segments || doorLayer.Segments || []).forEach(s => {
            const x1 = s.x1 !== undefined ? s.x1 : s.X1;
            const y1 = s.y1 !== undefined ? s.y1 : s.Y1;
            const x2 = s.x2 !== undefined ? s.x2 : s.X2;
            const y2 = s.y2 !== undefined ? s.y2 : s.Y2;
            ctx.moveTo(toCanvasX(x1), toCanvasY(y1));
            ctx.lineTo(toCanvasX(x2), toCanvasY(y2));
        });
        ctx.stroke();
    }

    // 4. Draw Raw Wall Layer in Gray
    if (wallLayer) {
        ctx.strokeStyle = '#5c6370';
        ctx.lineWidth = 1.4;
        ctx.beginPath();
        (wallLayer.segments || wallLayer.Segments || []).forEach(s => {
            const x1 = s.x1 !== undefined ? s.x1 : s.X1;
            const y1 = s.y1 !== undefined ? s.y1 : s.Y1;
            const x2 = s.x2 !== undefined ? s.x2 : s.X2;
            const y2 = s.y2 !== undefined ? s.y2 : s.Y2;
            ctx.moveTo(toCanvasX(x1), toCanvasY(y1));
            ctx.lineTo(toCanvasX(x2), toCanvasY(y2));
        });
        ctx.stroke();
    }

    // 5. Reconstruct Walls & Openings centered around building center
    const cxBldg = currentPdfCadData.buildingCenterX ?? currentPdfCadData.BuildingCenterX ?? ((t.bldgMinX + t.bldgMaxX) * 0.5);
    const cyBldg = currentPdfCadData.buildingCenterY ?? currentPdfCadData.BuildingCenterY ?? ((t.bldgMinY + t.bldgMaxY) * 0.5);
    const sFactor = currentBimScaleFactor || currentPdfCadData.scaleFactor || currentPdfCadData.ScaleFactor || (1 / 236.2205);

    const bimData = extractWallsFromCadLayer(wallLayer, winLayer, doorLayer, sFactor, cxBldg, cyBldg, wallHeight, forcedThick);
    currentGeneratedBimWalls = bimData.walls || [];
    currentGeneratedBimColumns = bimData.columns || [];

    // 6. Draw Reconstructed Walls in Bright Emerald Green with Opening Highlights
    ctx.lineWidth = 3.5;
    ctx.strokeStyle = '#40c057';
    currentGeneratedBimWalls.forEach(w => {
        const wx1 = cxBldg + (w.x1 / sFactor);
        const wy1 = cyBldg - (w.z1 / sFactor);
        const wx2 = cxBldg + (w.x2 / sFactor);
        const wy2 = cyBldg - (w.z2 / sFactor);

        ctx.beginPath();
        ctx.moveTo(toCanvasX(wx1), toCanvasY(wy1));
        ctx.lineTo(toCanvasX(wx2), toCanvasY(wy2));
        ctx.stroke();

        const wallLen = Math.hypot(w.x2 - w.x1, w.z2 - w.z1);
        if (wallLen > 1e-4 && w.openings) {
            const dirX = (w.x2 - w.x1) / wallLen;
            const dirZ = (w.z2 - w.z1) / wallLen;

            w.openings.forEach(op => {
                const opStartDist = op.dist;
                const opEndDist = op.dist + op.width;
                const p1x = cxBldg + ((w.x1 + dirX * opStartDist) / sFactor);
                const p1y = cyBldg - ((w.z1 + dirZ * opStartDist) / sFactor);
                const p2x = cxBldg + ((w.x1 + dirX * opEndDist) / sFactor);
                const p2y = cyBldg - ((w.z1 + dirZ * opEndDist) / sFactor);

                ctx.strokeStyle = op.type === 'GARAGE_DOOR' ? '#cc5de8' : op.type === 'DOOR' ? '#ff922b' : '#3bc9db';
                ctx.lineWidth = 5.0;
                ctx.beginPath();
                ctx.moveTo(toCanvasX(p1x), toCanvasY(p1y));
                ctx.lineTo(toCanvasX(p2x), toCanvasY(p2y));
                ctx.stroke();
            });
            ctx.strokeStyle = '#40c057';
            ctx.lineWidth = 3.5;
        }
    });

    // 6b. Draw Reconstructed Columns in Golden Amber with 'X' Cross
    currentGeneratedBimColumns.forEach(c => {
        const colCadX = cxBldg + (c.pos.x / sFactor);
        const colCadY = cyBldg - (c.pos.z / sFactor);
        const cadW = (c.width || 0.3) / sFactor;
        const cadD = (c.depth || c.width || 0.3) / sFactor;

        const left = toCanvasX(colCadX - cadW * 0.5);
        const top = toCanvasY(colCadY + cadD * 0.5);
        const right = toCanvasX(colCadX + cadW * 0.5);
        const bottom = toCanvasY(colCadY - cadD * 0.5);
        const boxW = right - left;
        const boxH = bottom - top;

        ctx.fillStyle = 'rgba(252, 196, 25, 0.28)';
        ctx.fillRect(left, top, boxW, boxH);
        ctx.strokeStyle = '#fcc419';
        ctx.lineWidth = 2.0;
        ctx.strokeRect(left, top, boxW, boxH);

        // 'X' cross
        ctx.beginPath();
        ctx.moveTo(left, top);
        ctx.lineTo(right, bottom);
        ctx.moveTo(left, bottom);
        ctx.lineTo(right, top);
        ctx.stroke();
    });

    // Update Counts
    let totalWins = 0;
    let totalDoors = 0;
    let totalGarage = 0;
    currentGeneratedBimWalls.forEach(w => {
        (w.openings || []).forEach(op => {
            if (op.type === 'GARAGE_DOOR') totalGarage++;
            else if (op.type === 'DOOR') totalDoors++;
            else totalWins++;
        });
    });

    const lblWall = document.getElementById('lbl-bim-wall-cnt');
    const lblCol = document.getElementById('lbl-bim-col-cnt');
    const lblWin = document.getElementById('lbl-bim-win-cnt');
    const lblDoor = document.getElementById('lbl-bim-door-cnt');
    const lblGarage = document.getElementById('lbl-bim-garage-cnt');
    if (lblWall) lblWall.textContent = String(currentGeneratedBimWalls.length);
    if (lblCol) lblCol.textContent = String(currentGeneratedBimColumns.length);
    if (lblWin) lblWin.textContent = String(totalWins);
    if (lblDoor) lblDoor.textContent = String(totalDoors);
    if (lblGarage) lblGarage.textContent = String(totalGarage);
}

function extractWallsFromCadLayer(wallLayer, winLayer, doorLayer, scale, cx, cy, wallHeight, forcedThick) {
    if (!wallLayer) return { walls: [], columns: [] };
    const segs = wallLayer.segments || wallLayer.Segments || [];
    if (segs.length === 0) return { walls: [], columns: [] };

    const TOLERANCE = 4.0;
    const hClusters = new Map();
    const vClusters = new Map();

    for (const s of segs) {
        const x1 = s.x1 !== undefined ? s.x1 : s.X1;
        const y1 = s.y1 !== undefined ? s.y1 : s.Y1;
        const x2 = s.x2 !== undefined ? s.x2 : s.X2;
        const y2 = s.y2 !== undefined ? s.y2 : s.Y2;

        const dx = Math.abs(x2 - x1);
        const dy = Math.abs(y2 - y1);
        if (dy <= 2.5 && dx >= 15.0) {
            const midY = (y1 + y2) * 0.5;
            let foundK = null;
            for (const k of hClusters.keys()) {
                if (Math.abs(k - midY) <= TOLERANCE) { foundK = k; break; }
            }
            if (foundK === null) { foundK = midY; hClusters.set(foundK, []); }
            hClusters.get(foundK).push([Math.min(x1, x2), Math.max(x1, x2)]);
        } else if (dx <= 2.5 && dy >= 15.0) {
            const midX = (x1 + x2) * 0.5;
            let foundK = null;
            for (const k of vClusters.keys()) {
                if (Math.abs(k - midX) <= TOLERANCE) { foundK = k; break; }
            }
            if (foundK === null) { foundK = midX; vClusters.set(foundK, []); }
            vClusters.get(foundK).push([Math.min(y1, y2), Math.max(y1, y2)]);
        }
    }

    // Merge touching/overlapping intervals per coordinate line (keeps gaps between distinct rooms/walls!)
    function mergeIntervals(list) {
        if (!list || list.length === 0) return [];
        const sorted = list.slice().sort((a, b) => a[0] - b[0]);
        const merged = [[sorted[0][0], sorted[0][1]]];
        for (let k = 1; k < sorted.length; k++) {
            const last = merged[merged.length - 1];
            const cur = sorted[k];
            if (cur[0] <= last[1] + 6.0) {
                last[1] = Math.max(last[1], cur[1]);
            } else {
                merged.push([cur[0], cur[1]]);
            }
        }
        return merged;
    }

    for (const [k, list] of hClusters.entries()) {
        hClusters.set(k, mergeIntervals(list));
    }
    for (const [k, list] of vClusters.entries()) {
        vClusters.set(k, mergeIntervals(list));
    }

    // 1. Collect all raw corridor segments between parallel lines within thickness range (8cm to 75cm)
    const rawHCorridors = [];
    const sortedHy = Array.from(hClusters.keys()).sort((a, b) => a - b);

    for (let i = 0; i < sortedHy.length; i++) {
        const y1 = sortedHy[i];
        const list1 = hClusters.get(y1);
        for (let j = i + 1; j < Math.Min ? Math.min(i + 8, sortedHy.length) : Math.min(i + 8, sortedHy.length); j++) {
            const y2 = sortedHy[j];
            const diff = y2 - y1;
            const thickM = diff * scale;
            if (thickM >= 0.08 && thickM <= 0.75) {
                const list2 = hClusters.get(y2);
                for (const int1 of list1) {
                    for (const int2 of list2) {
                        const ovStart = Math.max(int1[0], int2[0]);
                        const ovEnd = Math.min(int1[1], int2[1]);
                        if (ovEnd - ovStart >= 20.0) {
                            rawHCorridors.push({
                                y: (y1 + y2) * 0.5,
                                s: ovStart,
                                e: ovEnd,
                                thick: thickM
                            });
                        }
                    }
                }
            }
        }
    }

    const rawVCorridors = [];
    const sortedVx = Array.from(vClusters.keys()).sort((a, b) => a - b);

    for (let i = 0; i < sortedVx.length; i++) {
        const x1 = sortedVx[i];
        const list1 = vClusters.get(x1);
        for (let j = i + 1; j < Math.min(i + 8, sortedVx.length); j++) {
            const x2 = sortedVx[j];
            const diff = x2 - x1;
            const thickM = diff * scale;
            if (thickM >= 0.08 && thickM <= 0.75) {
                const list2 = vClusters.get(x2);
                for (const int1 of list1) {
                    for (const int2 of list2) {
                        const ovStart = Math.max(int1[0], int2[0]);
                        const ovEnd = Math.min(int1[1], int2[1]);
                        if (ovEnd - ovStart >= 20.0) {
                            rawVCorridors.push({
                                x: (x1 + x2) * 0.5,
                                s: ovStart,
                                e: ovEnd,
                                thick: thickM
                            });
                        }
                    }
                }
            }
        }
    }

    // Merge collinear corridor intervals on the same axis (within 8 CAD units = ~3.5 cm)
    function mergeCorridors(corridors, isH) {
        if (!corridors || corridors.length === 0) return [];
        const groups = [];
        const AXIS_TOL = 8.0;

        for (const c of corridors) {
            const pos = isH ? c.y : c.x;
            let grp = groups.find(g => Math.abs(g.axis - pos) <= AXIS_TOL);
            if (!grp) {
                grp = { axis: pos, items: [] };
                groups.push(grp);
            }
            grp.items.push(c);
        }

        const result = [];
        for (const grp of groups) {
            grp.items.sort((a, b) => a.s - b.s);
            let cur = null;
            for (const item of grp.items) {
                if (!cur) {
                    cur = { ...item };
                } else {
                    if (item.s <= cur.e + 10.0) {
                        cur.e = Math.max(cur.e, item.e);
                        cur.thick = Math.max(cur.thick, item.thick);
                    } else {
                        result.push(cur);
                        cur = { ...item };
                    }
                }
            }
            if (cur) result.push(cur);
        }
        return result;
    }

    const mergedH = mergeCorridors(rawHCorridors, true);
    const mergedV = mergeCorridors(rawVCorridors, false);

    const rawColumns = [];
    const rawHWalls = [];
    const rawVWalls = [];

    for (const h of mergedH) {
        const lenM = (h.e - h.s) * scale;
        const detectedThick = Math.round(h.thick * 100) / 100;
        const finalThick = forcedThick !== 'auto' ? parseFloat(forcedThick) : detectedThick;
        if (lenM <= 0.85 && h.thick <= 0.85 && Math.min(lenM, h.thick) / Math.max(lenM, h.thick) >= 0.70) {
            rawColumns.push({
                cx: (h.s + h.e) * 0.5,
                cy: h.y,
                w: detectedThick,
                d: Math.round(lenM * 100) / 100
            });
        } else {
            rawHWalls.push({
                y: h.y,
                x1: h.s,
                x2: h.e,
                thick: finalThick > 0.08 ? finalThick : 0.24
            });
        }
    }

    for (const v of mergedV) {
        const lenM = (v.e - v.s) * scale;
        const detectedThick = Math.round(v.thick * 100) / 100;
        const finalThick = forcedThick !== 'auto' ? parseFloat(forcedThick) : detectedThick;
        if (lenM <= 0.85 && v.thick <= 0.85 && Math.min(lenM, v.thick) / Math.max(lenM, v.thick) >= 0.70) {
            const midX = v.x;
            const midY = (v.s + v.e) * 0.5;
            const already = rawColumns.some(c => Math.hypot(c.cx - midX, c.cy - midY) < 35.0);
            if (!already) {
                rawColumns.push({
                    cx: midX,
                    cy: midY,
                    w: detectedThick,
                    d: Math.round(lenM * 100) / 100
                });
            }
        } else {
            rawVWalls.push({
                x: v.x,
                y1: v.s,
                y2: v.e,
                thick: finalThick > 0.08 ? finalThick : 0.24
            });
        }
    }

    const walls = [];
    let wCounter = 1;

    // Horizontal walls: strictly z1 === z2 (0% tilt guarantee!)
    for (const hw of rawHWalls) {
        walls.push({
            id: `wall_h_${wCounter++}`,
            x1: (hw.x1 - cx) * scale,
            z1: -(hw.y - cy) * scale,
            x2: (hw.x2 - cx) * scale,
            z2: -(hw.y - cy) * scale,
            thickness: hw.thick,
            height: wallHeight,
            type: 'H',
            openings: []
        });
    }

    // Vertical walls: strictly x1 === x2 (0% tilt guarantee!)
    for (const vw of rawVWalls) {
        walls.push({
            id: `wall_v_${wCounter++}`,
            x1: (vw.x - cx) * scale,
            z1: -(vw.y1 - cy) * scale,
            x2: (vw.x - cx) * scale,
            z2: -(vw.y2 - cy) * scale,
            thickness: vw.thick,
            height: wallHeight,
            type: 'V',
            openings: []
        });
    }

    // 1D Axis-Invariant Corner Snapping
    const snapM = 0.38;
    for (const hw of walls) {
        if (hw.type !== 'H') continue;
        for (const vw of walls) {
            if (vw.type !== 'V') continue;
            const minVz = Math.min(vw.z1, vw.z2);
            const maxVz = Math.max(vw.z1, vw.z2);
            if (hw.z1 >= minVz - snapM && hw.z1 <= maxVz + snapM) {
                if (Math.abs(hw.x1 - vw.x1) <= snapM) hw.x1 = vw.x1;
                if (Math.abs(hw.x2 - vw.x1) <= snapM) hw.x2 = vw.x1;
            }
        }
    }

    for (const vw of walls) {
        if (vw.type !== 'V') continue;
        for (const hw of walls) {
            if (hw.type !== 'H') continue;
            const minHx = Math.min(hw.x1, hw.x2);
            const maxHx = Math.max(hw.x1, hw.x2);
            if (vw.x1 >= minHx - snapM && vw.x1 <= maxHx + snapM) {
                if (Math.abs(vw.z1 - hw.z1) <= snapM) vw.z1 = hw.z1;
                if (Math.abs(vw.z2 - hw.z1) <= snapM) vw.z2 = hw.z1;
            }
        }
    }

    // Columns
    const columns = [];
    let colCounter = 1;
    for (const rc of rawColumns) {
        columns.push({
            id: `col_${colCounter++}`,
            pos: {
                x: Math.round((rc.cx - cx) * scale * 1000) / 1000,
                z: Math.round(-(rc.cy - cy) * scale * 1000) / 1000
            },
            width: rc.w,
            depth: rc.d,
            height: wallHeight,
            round: false
        });
    }

    // Helper distance
    const dist2D = (x1, z1, x2, z2) => Math.hypot(x2 - x1, z2 - z1);

    // 3. Extract Openings (Doors & Windows)
    let opCounter = 1;
    const doorSpecs = [];
    const dTexts = (doorLayer?.texts || doorLayer?.Texts || []);
    for (let i = 0; i < dTexts.length; i++) {
        const t = dTexts[i];
        if (typeof t === 'string' && t.startsWith('D') && i + 2 < dTexts.length) {
            const dw = parseFloat(dTexts[i + 1]);
            const dh = parseFloat(dTexts[i + 2]);
            if (!isNaN(dw) && !isNaN(dh)) {
                doorSpecs.push({ label: t, w: dw / 100, h: dh / 100, x: null, y: null });
                i += 2;
            }
        }
    }

    const textItems = doorLayer?.textItems || doorLayer?.TextItems || [];
    for (let i = 0; i < doorSpecs.length; i++) {
        const found = textItems.find(ti => (ti.text || ti.Text) === doorSpecs[i].label);
        if (found) {
            doorSpecs[i].x = found.x !== undefined ? found.x : found.X;
            doorSpecs[i].y = found.y !== undefined ? found.y : found.Y;
        }
    }

    let specIdx = 0;
    for (const spec of doorSpecs) {
        const wM = spec.w > 0.6 ? spec.w : 0.90;
        const hM = spec.h > 1.5 ? spec.h : 2.05;
        let targetWall = null;
        let bestDist = 0;

        if (spec.x !== null && spec.y !== null) {
            const dxWorld = (spec.x - cx) * scale;
            const dzWorld = -(spec.y - cy) * scale;
            let minPerp = Infinity;

            for (const w of walls) {
                const wLen = dist2D(w.x1, w.z1, w.x2, w.z2);
                if (wLen < wM + 0.2) continue;
                const dirX = (w.x2 - w.x1) / wLen;
                const dirZ = (w.z2 - w.z1) / wLen;
                const proj = (dxWorld - w.x1) * dirX + (dzWorld - w.z1) * dirZ;
                if (proj >= 0.1 && proj <= wLen - 0.1) {
                    const perp = Math.abs((dxWorld - w.x1) * dirZ - (dzWorld - w.z1) * dirX);
                    if (perp < minPerp && perp < 1.2) {
                        minPerp = perp;
                        targetWall = w;
                        bestDist = Math.max(0.1, Math.min(wLen - wM - 0.1, proj - wM * 0.5));
                    }
                }
            }
        }

        if (!targetWall) {
            const candidates = walls.filter(w => dist2D(w.x1, w.z1, w.x2, w.z2) >= 1.6 && !w.openings.some(o => o.type === 'DOOR'));
            if (candidates.length > 0) {
                targetWall = candidates[specIdx % candidates.length];
                const wLen = dist2D(targetWall.x1, targetWall.z1, targetWall.x2, targetWall.z2);
                bestDist = Math.max(0.2, (wLen - wM) * 0.35);
            }
        }

        if (targetWall) {
            const isGarage = (spec.label && (spec.label.startsWith('BRAMA') || spec.label.startsWith('GARAZ'))) || wM >= 2.20;
            targetWall.openings.push({
                id: `op_door_${opCounter++}`,
                type: isGarage ? 'GARAGE_DOOR' : 'DOOR',
                dist: Math.round(bestDist * 100) / 100,
                width: Math.round(wM * 100) / 100,
                height: isGarage ? 2.15 : Math.round(hM * 100) / 100,
                sill: 0.0
            });
            specIdx++;
        }
    }

    // Windows
    const winSegs = winLayer?.segments || winLayer?.Segments || [];
    if (winSegs.length > 0) {
        const winCandidates = [];
        for (const s of winSegs) {
            const x1 = s.x1 !== undefined ? s.x1 : s.X1;
            const y1 = s.y1 !== undefined ? s.y1 : s.Y1;
            const x2 = s.x2 !== undefined ? s.x2 : s.X2;
            const y2 = s.y2 !== undefined ? s.y2 : s.Y2;
            const len = Math.hypot(x2 - x1, y2 - y1) * scale;
            if (len >= 0.75 && len <= 5.5) {
                winCandidates.push({ x: (x1 + x2) * 0.5, y: (y1 + y2) * 0.5, len });
            }
        }

        const winClusters = [];
        for (const c of winCandidates) {
            const wx = (c.x - cx) * scale;
            const wz = -(c.y - cy) * scale;
            let merged = false;
            for (const wc of winClusters) {
                if (dist2D(wc.x, wc.z, wx, wz) < 0.6) {
                    wc.x = (wc.x + wx) * 0.5;
                    wc.z = (wc.z + wz) * 0.5;
                    wc.width = Math.max(wc.width, c.len);
                    merged = true;
                    break;
                }
            }
            if (!merged) {
                winClusters.push({ x: wx, z: wz, width: c.len });
            }
        }

        for (const wc of winClusters) {
            let bestWall = null;
            let minPerp = Infinity;
            let bestDist = 0;

            for (const w of walls) {
                const wLen = dist2D(w.x1, w.z1, w.x2, w.z2);
                if (wLen < wc.width + 0.3) continue;
                const dirX = (w.x2 - w.x1) / wLen;
                const dirZ = (w.z2 - w.z1) / wLen;
                const proj = (wc.x - w.x1) * dirX + (wc.z - w.z1) * dirZ;
                if (proj >= 0.2 && proj <= wLen - 0.2) {
                    const perp = Math.abs((wc.x - w.x1) * dirZ - (wc.z - w.z1) * dirX);
                    if (perp < minPerp && perp < 0.6) {
                        minPerp = perp;
                        bestWall = w;
                        bestDist = Math.max(0.15, Math.min(wLen - wc.width - 0.15, proj - wc.width * 0.5));
                    }
                }
            }

            if (bestWall && !bestWall.openings.some(o => Math.abs(o.dist - bestDist) < 0.8)) {
                const isGarage = wc.width >= 2.20;
                bestWall.openings.push({
                    id: `op_win_${opCounter++}`,
                    type: isGarage ? 'GARAGE_DOOR' : 'WINDOW',
                    dist: Math.round(bestDist * 100) / 100,
                    width: Math.round(wc.width * 100) / 100,
                    height: isGarage ? 2.15 : 1.40,
                    sill: isGarage ? 0.0 : 0.85
                });
            }
        }
    }

    // Fallback windows for long walls without openings
    walls.forEach(w => {
        const wallLen = dist2D(w.x1, w.z1, w.x2, w.z2);
        if (wallLen >= 2.6 && w.openings.length === 0) {
            const winW = wallLen >= 4.0 ? 1.80 : 1.20;
            w.openings.push({
                id: `op_win_${opCounter++}`,
                type: 'WINDOW',
                dist: Math.round((wallLen - winW) * 0.5 * 100) / 100,
                width: winW,
                height: 1.40,
                sill: 0.85
            });
        }
    });

    return { walls, columns };
}

// Initialize Scene & Start Render Loop
initRibbonUI();
initContextMenuAndModals();
initAutoBimModal();
applyTimeOfDay(projectData.timeOfDayHours);
selectTool('SELECT');
syncAllFloorsButtonUI();
updateCameraTransform();
rebuildEntireScene();
requestAnimationFrame(animate);
