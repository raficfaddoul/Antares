import { Viewer, Product, State, ViewType, RenderingMode, ProductType, NavigationCube, Grid, EasingType, MessageProgress, InteractiveClippingPlane } from '@xbim/viewer';
import { CameraType } from '@xbim/viewer';
import { Viewpoint } from '@xbim/viewer';
import { vec3, mat4, ho } from 'gl-matrix';
import { PerformanceRating } from '@xbim/viewer';
import { ClippingPlane } from '@xbim/viewer';
import { LoaderOverlay } from '@xbim/viewer';
import { ProductAnalyticalResult } from '@xbim/viewer';

import { DxfParser } from "dxf-parser";
/*import { Viewer as ThreeDxfViewer } from "three-dxf";*/
import { DXFViewer } from 'three-dxf-viewer';


//import { Scene, WebGLRenderer, OrthographicCamera, Color } from 'three';

//let scene, renderer, camera;  // These are now scoped globally within this module.



let GlobalObjRef;
let globalViewer;

// tslint:disable-next-line: only-arrow-functions
//var QueryString = function () {
//    // This function is anonymous, is executed immediately and 
//    // the return value is assigned to QueryString!
//    var queryString = {};
//    var query = window.location.search.substring(1);
//    var vars = query.split("&");
//    for (var i = 0; i < vars.length; i++) {
//        var pair = vars[i].split("=");
//        // If first entry with this name
//        if (typeof queryString[pair[0]] === "undefined") {
//            queryString[pair[0]] = pair[1];
//            // If second entry with this name
//        } else if (typeof queryString[pair[0]] === "string") {
//            var arr = [queryString[pair[0]], pair[1]];
//            queryString[pair[0]] = arr;
//            // If third or later entry with this name
//        } else {
//            queryString[pair[0]].push(pair[1]);
//        }
//    }
//    return queryString;
//}();


//node_modules/@xbim/viewer/src/c";

//window.rafic = () => {
//    var viewer = new Viewer('viewer');
//    viewer.on('loaded', () => {
//        viewer.show(ViewType.DEFAULT);
//    });
//    viewer.load('../data/SampleHouse.wexbim');
//    viewer.start();
//}

"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var viewer_1 = require("@xbim/viewer");
var camera_1 = require("@xbim/viewer");
var viewpoint_1 = require("@xbim/viewer");
var performance_rating_1 = require("@xbim/viewer");
var loader_overlay_1 = require("@xbim/viewer");
// tslint:disable-next-line: only-arrow-functions
var t = 7;
/*var QueryString = function () {*/
    // This function is anonymous, is executed immediately and
    // the return value is assigned to QueryString!
//    var queryString = {};
//    var query = window.location.search.substring(1);
//    var vars = query.split("&");
//    for (var i = 0; i < vars.length; i++) {
//        var pair = vars[i].split("=");
//        // If first entry with this name
//        if (typeof queryString[pair[0]] === "undefined") {
//            queryString[pair[0]] = pair[1];
//            // If second entry with this name
//        }
//        else if (typeof queryString[pair[0]] === "string") {
//            var arr = [queryString[pair[0]], pair[1]];
//            queryString[pair[0]] = arr;
//            // If third or later entry with this name
//        }
//        else {
//            queryString[pair[0]].push(pair[1]);
//        }
//    }
//    return queryString;
//}();
//node_modules/@xbim/viewer/src/";
//window.rafic = function (fileName) {
//    var viewer = new viewer_1.Viewer('viewer');
//    viewer.on('loaded', function () {
//        viewer.show(viewer_1.ViewType.DEFAULT);
//    });
//    fetch('../data/file.txt')
//        .then(response => response.text())
//        .then(text => console.log(text))
//  // outputs the content of the text file
//    viewer.load(fileName);
//    viewer.start();
//};


window.renderDxf = function (dxfUrl) {
    var parser = new DxfParser();
    var dxfCanvas;

    fetch(dxfUrl)
        .then(res => res.text())
        .then(text => {
            var dxfFile = parser.parseSync(text);
            dxfCanvas = new ThreeDxfViewer(dxfFile, document.getElementById("dxf-canvas"), 400, 400);
        });
}

let fontUrlGlobal;
let containerIdGlobal;

// Function to initialize the viewer with parameters from Blazor
function initializeViewerSettings(fontUrl, containerId) {
    fontUrlGlobal = fontUrl;
    containerIdGlobal = containerId;
}

// Separate function to handle the file load and viewer setup
async function handleFileLoad(event) {
    const file = event.target.files[0];
  

    //const fontLoader = new THREE.FontLoader();
    /*fontLoader.load(fontUrlGlobal, async function (font) {*/
        let dxf = await new DXFViewer().getFromFile(file, 'fonts/helvetiker_regular.typeface.json');

        let scene = new THREE.Scene();
        scene.add(dxf);

        let container = document.getElementById(containerIdGlobal);
        let width = container.clientWidth;
        let height = container.clientHeight;

        let renderer = new THREE.WebGLRenderer();
        renderer.setSize(width, height);
        container.appendChild(renderer.domElement);

        let camera = new THREE.PerspectiveCamera(75, width / height, 0.1, 1000);
        camera.position.z = 5;

        function animate() {
            requestAnimationFrame(animate);
            renderer.render(scene, camera);
        }
        animate();
  /*  });*/
}

// Initialize event listener on page load
document.addEventListener('DOMContentLoaded', function () {
    const fileInputElement = document.getElementById('dxfFileInput');
    if (fileInputElement) {
        fileInputElement.addEventListener('change', handleFileLoad);
    }
});

// Expose the initialization function to Blazor
window.dxfViewerInterop = {
    initializeSettings: initializeViewerSettings
};

window.initializeDXFViewer = function (canvasElementId) {
    const canvasElement = document.getElementById(canvasElementId);
    scene = new Scene();
    renderer = new WebGLRenderer({ canvas: canvasElement });
    renderer.setSize(window.innerWidth, window.innerHeight);
    scene.background = new Color(0x212830);

    const size = 5000;
    const aspect = canvasElement.offsetWidth / canvasElement.offsetHeight;
    camera = new OrthographicCamera(-size * aspect, size * aspect, size, -size, 1, 10000);
    camera.position.z = 5000;

    function animate() {
        requestAnimationFrame(animate);
        renderer.render(scene, camera);
    }

    animate();
}

window.loadDXF = function (base64Data) {
    const dxfBlob = new Blob([Uint8Array.from(atob(base64Data), c => c.charCodeAt(0))], { type: 'application/dxf' });
    const url = URL.createObjectURL(dxfBlob);

    new DXFViewer().getFromFile(url)
        .then(dxf => {
            scene.add(dxf);
            URL.revokeObjectURL(url); // Clean up the URL object
        })
        .catch(error => {
            console.error('Error loading DXF file:', error);
        });
}

window.testdxf = async function () {
    let dxf = await new DXFViewer().getFromPath('/2010.dxf', '/fonts/helvetiker_regular.typeface.json');

    let scene = new THREE.Scene();
    scene.addDXF(dxf);
}






window.HighlightElement = (instanceIds) => {
    //    var span = document.getElementById("ddebug");
    //if (span) {
    //    span.innerText = instanceIds.toString();
    //}

    globalViewer.setState(253, instanceIds);
}

window.definedStyles = [];

window.ColorElement = (styleNum, entityId, red, green, blue) => {
    // Define a color
    var colour = [red, green, blue, 255];

    // Check if the style number is already defined
    if (!window.definedStyles.includes(styleNum)) {
        viewer.defineStyle(styleNum, colour);
        // Add style number to the list of defined styles
        window.definedStyles.push(styleNum);
    }

    // Set the style for the entity
    viewer.setStyle(styleNum, entityId);
};

window.ResetStates = () => {

    if (!globalViewer) return;

    globalViewer.clearHighlighting();
}


//window.AddEquipementTags = () => {

//    if (!globalViewer) return;

//    AddEquipementTag(position, `Tag for ID: ${id}`, id);
//}

//window.AddEquipementTag = (x, y, z, base64Image,text) => {

//    if (!globalViewer) return;
//    const positiont = vec3.fromValues(x, y, z);
//    const position = viewer.getHtmlCoordinatesOfVector(positiont);

//    const div = document.createElement('div');
//   div.className = 'tag';
//    div.textContent = text;
//    div.style.left = (position[0] - div.clientWidth / 2) + 'px';
//    div.style.top = (position[1] - div.clientHeight / 2) + 'px';

//    div.dataset.positiont = positiont;
//    //var span = document.getElementById("ids");
//    //span.innerHTML = "Product id: " + text + ", model: " + viewer.pMatrix;
//    document.getElementById('viewerContainer').appendChild(div);
//    GlobalObjRef.invokeMethodAsync('Report', position[0], position[1]);/// rafic added

//}

window.removeAllEquipmentTags = () => {
    // Get all elements with the class 'tag'
    const tags = document.getElementsByClassName('tag');

    // Create an array from the HTMLCollection to safely iterate and remove elements
    Array.from(tags).forEach(tag => {
        tag.remove(); // Remove each 'tag' element
    });
}

window.AddEquipmentTag = (x, y, z, base64Image, name, description) => {
    if (!globalViewer) return;

    // Convert coordinates
    const positiont = vec3.fromValues(x, y, z);
    const position = viewer.getHtmlCoordinatesOfVector(positiont);

    // Create the container div
    const div = document.createElement('div');
    div.className = 'tag';
    div.style.cssText = 'display: flex; align-items: center; position: absolute; background-color: transparent;';
    div.style.left = (position[0] - div.clientWidth / 2) + 'px';
    div.style.top = (position[1] - div.clientHeight / 2) + 'px';
    div.style.pointerEvents = 'auto';

    const img = document.createElement('img');
    img.src = `data:image/jpeg;base64,${base64Image}`;
    img.style.flex = 'none'; // Prevent flexbox from resizing the image
    div.appendChild(img);
    div.title = name + ": " + description;
    const textNode = document.createTextNode(name);
    div.appendChild(textNode);

    div.dataset.clicked = 'false'; // Initial state

    div.dataset.positiont = positiont; 

    div.addEventListener('click', function () {
        if (this.dataset.clicked === 'true') {
            // If clicked before, remove this tag
            this.remove();
        } else {
            // Mark as clicked
            this.dataset.clicked = 'true';
        }
    });
  
    document.getElementById('viewerContainer').appendChild(div);

    GlobalObjRef.invokeMethodAsync('Report', position[0], position[1]);
}


window.updateTags = () => {
    document.querySelectorAll('.tag').forEach(tag => {
        const positiontString = tag.dataset.positiont; // Ensure you store the id in the dataset when creating the tag
        var positiontArray = positiontString.split(',').map(Number);

        const position = viewer.getHtmlCoordinatesOfVector(positiontArray);
        tag.style.left = (position[0] - tag.clientWidth / 2) + 'px';
        tag.style.top = (position[1] - tag.clientHeight / 2) + 'px';
    });
    window.requestAnimationFrame(updateTags);
}
window.requestAnimationFrame(updateTags);

//it('should passes when used properly', () => {
//    <Camera
//        type={Constants.Type.front}
//        flashMode={Constants.FlashMode.off}
//        autoFocus={Constants.AutoFocus.on}
//        whiteBalance={Constants.WhiteBalance.incandescent}
//        videoStabilizationMode={Constants.VideoStabilization.cinematic}
//    />;
//});




window.rafic = function (objRef, fileName) {
    GlobalObjRef = objRef;

    var viewer = new Viewer("viewer");
    globalViewer = viewer;

    var overlay = new LoaderOverlay();
    viewer.addPlugin(overlay);
    overlay.show();

    viewer.cameraProperties.fov = 53;
    var types = ProductType;
    var states = State;

    window['allTypes'] = Object.getOwnPropertyNames(types).filter(n => !isNaN(Number(n))).map(id => parseInt(id, 10));

    //make these global for the page
    window['viewer'] = viewer;
    window['types'] = types;
    window['states'] = states;
    window['RenderingMode'] = RenderingMode;
    window['ViewType'] = ViewType;
    window['product'] = Product;
    window['CameraType'] = CameraType;
    window['PerformanceRating'] = PerformanceRating;
    window['EasingType'] = EasingType;
    window['overlay'] = overlay;
    window['Viewpoint'] = Viewpoint;

    viewer.background = [0, 0, 0, 0];
    viewer.hoverPickEnabled = true;
    viewer.on("error", function (arg) {
        var container = viewer.canvas.parentNode;
        if (container) {
            // Prepend an error report to the beginning of the container's inner HTML
            container.innerHTML = "<pre style='color:red;'>" + arg.message + "</pre>" + container.innerHTML;
        }
    });

    //var modelId = QueryString["model"];
    //var model = "";

    //var span = document.getElementById("ddebug");
    //if (span) {
    //    span.innerText = modelId;
    //}

    //if (typeof (modelId) == "undefined") {
    //    model = "../data/SampleHouse.wexbim";
    //}
    //else {
    //    model = "/tests/data/" + modelId + ".wexbim";
    //}
    //if (modelId != null && modelId === 'v4') {
    //    model = modelId;
    //}




    viewer.on("pick", function (arg) {

        const id = arg.id; // ID of the picked element, if any
    //    const position = viewer.getHTMLPositionOfProductCentroid(id, 1);
       // const positiont = vec3.fromValues(arg.xyz[0], arg.xyz[1], arg.xyz[2]);
       // const position = viewer.getHtmlCoordinatesOfVector(positiont);
       // addTag(position, `Tag for ID: ${id}`, id);

    
    var span = document.getElementById("ids");
        span.innerHTML = `Product id: ${arg.id}, model: ${arg.model}`;

    if (arg && arg.xyz) {
        var span = document.getElementById("coords");
        const c = arg.xyz;
        span.innerHTML = `Click in 3D: [${c[0].toFixed(2)}, ${c[1].toFixed(2)}, ${c[2].toFixed(2)}]`;
    }

    var state = viewer.getState(arg.id, arg.model);
    if (state === State.HIGHLIGHTED) {
        viewer.removeState(State.HIGHLIGHTED, [arg.id], arg.model);
    } else {
        viewer.addState(State.HIGHLIGHTED, [arg.id], arg.model);
    }
       
        var state1 = viewer.getState(arg.id, arg.model);
        var c;
        if (arg && arg.xyz) {
            c = arg.xyz;
        }
        else {
            c =[-1,-1,-1]
        }
        GlobalObjRef.invokeMethodAsync('GetElementID', arg.id, state1, State.HIGHLIGHTED, c[0], c[1], c[2]);/// rafic added
    });

    //function getHtmlCoordinatesOfVector(position){
    //    if (!position) return [];

    //    const transform = mat4.multiply(mat4.create(), viewer.pMatrix, viewer.mvMatrix);
    //    const glPosition = vec3.transformMat4(vec3.create(), position, transform);

    //    const glX = glPosition[0];
    //    const glY = glPosition[1];

    //    const htmlX = (glX + 1) / 2.0 * viewer.width;
    //    const htmlY = viewer.height - (glY + 1) / 2.0 * viewer.height;

    //    return [htmlX, htmlY];
    //}

    function addTag(position, text, id) {
        const div = document.createElement('div');
        div.className = 'tag';
        div.textContent = text;
        div.style.left = (position[0] - div.clientWidth / 2) + 'px';
        div.style.top = (position[1] - div.clientHeight / 2) + 'px';

        div.dataset.id = id;  // This sets the data-id attribute on the div
        var span = document.getElementById("ids");
        span.innerHTML = "Product id: " + text + ", model: " + div.style.top;
        document.getElementById('viewerContainer').appendChild(div);
    }

    //function updateTags() {
    //    document.querySelectorAll('.tag').forEach(tag => {
    //        const id = tag.dataset.id; // Ensure you store the id in the dataset when creating the tag
    //        const position = viewer.getHTMLPositionOfProductCentroid(id, 1);
    //        tag.style.left = (position[0] - tag.clientWidth / 2) + 'px';
    //        tag.style.top = (position[1] - tag.clientHeight / 2) + 'px';
    //    });
    //    window.requestAnimationFrame(updateTags);
    //}
    //window.requestAnimationFrame(updateTags);

    viewer.on("hoverpick", function (arg) {
        var span = document.getElementById("hoverid");

        if (arg && arg.id) {
            span.innerHTML = `Product id: ${arg.id}, model: ${arg.model}`;
            if (arg.xyz) {
                var span = document.getElementById("hovercoords");
                const c = arg.xyz;
                span.innerHTML = `[${c[0].toFixed(2)}, ${c[1].toFixed(2)}, ${c[2].toFixed(2)}]`;
            }
        } else {
            span.innerHTML = "";
        }
    });
    viewer.on("dblclick", (arg) => {
        viewer.addState(State.HIDDEN, [arg.id], arg.model);
    });
    viewer.on("fps", (fps) => {
        var span = document.getElementById("fps");
        if (span) {
            span.innerText = `${fps}, performance: ${PerformanceRating[viewer.performance]}`;
        }
    });
    viewer.on("pointerlockchange", (arg) => {
        console.log('canvas Pointer lock change', arg);
    });

    //var viewer2 = new Viewer("viewer2");
    //var sync = () => {
    //    viewer2.mvMatrix = viewer.mvMatrix;
    //    window.requestAnimationFrame(sync);
    //};
  //  window.requestAnimationFrame(sync);
    //viewer.on('navigationEnd', () => {
    //    cube.stopped = true;
    //    let d = document.getElementById('snapshot');
    //    let img = viewer.getCurrentImageHtml(400, 200);
    //    d.innerHTML = '';
    //    d.appendChild(img);
    //    cube.stopped = false;
    //})

    // viewer.on("loaded", () => {
    // let image = viewer.getCurrentImageHtml(2000, 1000);
    // image.style.width = '100%';
    // let initialImage = document.getElementById("initialSnapshot");
    // initialImage.appendChild(image);
    // viewer.startRotation();
    // });
    //var span = document.getElementById("fpt");
    //if (span) {
    //    span.innerHTML = "0";
    //}

    var versionSpan = document.getElementById("webglVersion");
    versionSpan.innerHTML = viewer.glVersion.toString();

    var progress = document.getElementById("progress");


    viewer.on('loaded', () => {
        viewer.start();
        //viewer2.start();
        overlay.hide();
        viewer.show(ViewType.DEFAULT, undefined, undefined, false);
        cube.stopped = false;
    });

    viewer.readerOptions.orderGeometryBySize = true;

    //if (modelId == 'large') {
    //    // load context (mostly large objects)
    //    viewer.loadAsync('/tests/data/large/context/roofing.wexbim');
    //    viewer.loadAsync('/tests/data/large/context/site-overall site.wexbim');
    //    viewer.loadAsync('/tests/data/large/context/spec wall ceiling.wexbim');
    //    viewer.loadAsync('/tests/data/large/context/steel-cladding.wexbim');
    //    // load heavy detailed model
    //    viewer.loadAsync('/tests/data/large/steel-deck&scuppers.wexbim');
    //}
/*    else {*/  

    viewer.loadAsync(fileName, "base", null, (msg) => {
        progress.innerHTML = `[${MessageProgress(msg).toFixed(2)}%] ${msg.message}`;
    });
   // viewer2.loadAsync(fileName);
    
 /*   }*/


    var grid = new Grid();
    grid.zFactor = 20;
    grid.colour = [0, 0, 0, 0.8];
    viewer.addPlugin(grid);

    var cube = new NavigationCube();
    cube.ratio = 0.05;
    cube.passiveAlpha = cube.activeAlpha = 1.0;
    cube.minSize = 75;
    viewer.addPlugin(cube);

    var plane = new InteractiveClippingPlane();
    viewer.addPlugin(plane);

    viewer.defineStyle(0, [255, 0, 0, 255]);  //red
    viewer.defineStyle(1, [0, 0, 255, 100]);  //semitransparent blue
    viewer.defineStyle(2, [255, 255, 255, 255]); //white

    viewer.highlightingColour = [0, 0, 225, 200];
    document['makeWallsRed'] = () => {
        viewer.setStyle(0, types.IFCWALLSTANDARDCASE);
        viewer.setStyle(0, types.IFCCURTAINWALL);
        viewer.setStyle(0, types.IFCWALL);
    };
    document['selectAllWalls'] = () => {
        viewer.addState(State.HIGHLIGHTED, types.IFCWALLSTANDARDCASE);
        viewer.addState(State.HIGHLIGHTED, types.IFCCURTAINWALL);
        viewer.addState(State.HIGHLIGHTED, types.IFCWALL);
    };
    document['hideWalls'] = () => {
        viewer.addState(State.HIDDEN, types.IFCWALLSTANDARDCASE);
        viewer.addState(State.HIDDEN, types.IFCCURTAINWALL);
        viewer.addState(State.HIDDEN, types.IFCWALL);
    };
    document['resetWalls'] = () => {
        viewer.resetState(types.IFCWALLSTANDARDCASE);
        viewer.resetState(types.IFCCURTAINWALL);
        viewer.resetState(types.IFCWALL);
    };

    document['clip'] = () => {
        plane.stopped = false;
    };
    document['hideClippingControl'] = () => {
        plane.stopped = true;
    };
    document['unclip'] = () => {
        viewer.unclip();
        plane.stopped = true;
    };
    document['stopCube'] = () => {
        cube.stopped = true;
    };
    document['startCube'] = () => {
        cube.stopped = false;
    };
    document['stopGrid'] = () => {
        grid.stopped = true;
    };
    document['startGrid'] = () => {
        grid.stopped = false;
    };
    document['takeSnapshot'] = () => {
        cube.stopped = true;


        const view = Viewpoint.GetViewpoint(viewer, null);

        var img = document.createElement('img');
        img.src = 'data:image/png;base64,' + view.snapshot.snapshot_data;
        img.style.width = "100%";
        img.style.cursor = "pointer";

        var place = document.getElementById("snapshot");
        place.innerHTML = "";
        place.appendChild(img);

        img.onclick = () => {
            Viewpoint.SetViewpoint(viewer, view, null, 1000);
            place.innerHTML = "";
        };

        cube.stopped = false;
    };

    document['updateGamma'] = function (evt) {
        const input = evt.target;
        if (input && input.value) {  // Added a check to ensure input and input.value are defined
            const value = parseFloat(input.value);
            if (!isNaN(value)) { // Ensure value is a valid number
                viewer.gamma = value;
            }
        }
    };

    document['updateContrast'] = function (evt) {
        var input = evt.target;
        if (input instanceof HTMLInputElement) { // Ensuring the event target is an input element
            var value = parseFloat(input.value);
            if (!isNaN(value)) { // Check if the parsed value is a valid number
                viewer.contrast = value;
            }
        }
    };

    document['updateBrightness'] = function (evt) {
        var input = evt.target;
        if (input instanceof HTMLInputElement) { // Ensuring the event target is an input element
            var value = parseFloat(input.value);
            if (!isNaN(value)) { // Check if the parsed value is a valid number
                viewer.brightness = value;
            }
        }
    };


    document['zoomToSelection'] = () => {
        const elements = viewer.getProductsWithState(State.HIGHLIGHTED);
        viewer.zoomTo(elements);
    };


    viewer.on("pick", (args) => {
        viewer.stopRotation();
        var id = args.id;
        var radios = document.getElementsByName("radioHiding");
        for (var i in radios) {
            if (radios.hasOwnProperty(i)) {
                var radio = radios[i];
                if (radio.checked) {
                    var val = radio.value;
                    if (val === "noHiding") {
                        return;
                    }
                    if (val === "hideOne") {
                        viewer.addState(State.HIDDEN, [id]);
                    }
                    if (val === "hideType") {
                        var type = viewer.getProductType(id);
                        viewer.addState(State.HIDDEN, type);
                    }
                    break;
                }
            }
        }

        //viewer.zoomTo(id);
    });

    window['clipBox'] = function () {
        var planes = [
            {
                direction: [1, 0, 0],
                location: [3000, 0, 0]
            },
            {
                direction: [0, 1, 0],
                location: [0, 2000, 0]
            },
            {
                direction: [0, 0, 1],
                location: [0, 0, 1000]
            },
            {
                direction: [-1, 0, 0],
                location: [-3000, 0, 0]
            },
            {
                direction: [0, -1, 0],
                location: [0, -2000, 0]
            },
            {
                direction: [0, 0, -1],
                location: [0, 0, -1000]
            }
        ];

        viewer.sectionBox.setToPlanes(planes);
        viewer.zoomTo();
    };


    window['releaseClipBox'] = () => {
        viewer.sectionBox.clear();
        viewer.zoomTo();
    };
    //// restore init script if any is saved
    //var script = localStorage.getItem('initScript-' + modelId);
    //var scriptArea = document.getElementById('initScript');
    //if (script) {
    //    scriptArea.value = script;
    //    viewer.on('loaded', function () {
    //        eval(script);
    //    });
    //}
    //// persist script before loads
    //window.addEventListener("beforeunload", function (event) {
    //    localStorage.setItem('initScript-' + modelId, scriptArea.value);
    //});

   

};
//sourceMappingURL = file.js.map