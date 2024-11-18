window.dxfViewerInterop = {
    loadDXF: async function (fileInputId, fontUrl, containerId) {
        let fileInput = document.getElementById(fileInputId);
        let file = fileInput.files[0];

        const fontLoader = new THREE.FontLoader();

        fontLoader.load(fontUrl, async function (font) {
            let viewer = new window.ThreeDXF.DXFViewer();  // Use window.ThreeDXF.DXFViewer for the global object
            let dxf = await viewer.getFromFile(file, font);

            let scene = new THREE.Scene();
            scene.add(dxf);

            let container = document.getElementById(containerId);
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
        });
    }
};