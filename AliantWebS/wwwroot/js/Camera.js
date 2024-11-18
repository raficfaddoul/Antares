let canvas;
let stream;

function setBlazorPageReference(blazorPageHook) {
    window.blazorPageHook = blazorPageHook;
}

window.disposeCamera = () => {

    videoElement.srcObject.getVideoTracks().forEach(track => track.stop());
}




window.capture = (capturePreview) => {


    canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');


    canvas.width = window.highestResolutionTrack.getSettings().width;
    canvas.height = window.highestResolutionTrack.getSettings().height;

    // Draw the video frame to the canvas.
    context.drawImage(videoElement, 0, 0, canvas.width, canvas.height);

    if (capturePreview) {
        const canvasPreview = document.getElementById('canvasPreview');
        const contextPreview = canvasPreview.getContext('2d');
        // Draw the video frame to the canvas.
        contextPreview.drawImage(videoElement, 0, 0, canvasPreview.width, canvasPreview.height);
    }

}



window.SaveImage = (fileName) => {
    //const canvas = document.getElementById('canvas');
    //const context = canvas.getContext('2d');
    //// Draw the video frame to the canvas.
    //context.drawImage(player, 0, 0, canvas.width, canvas.height);


    //let file = null;
    //let blob = document.querySelector("#canvas").toBlob(function (blob) {
    //    file = new File([blob], 'test.png', { type: 'image/png' });
    //}, 'image/png');

    // JPEG file
    let file = null;

    //const canvas = document.getElementById('canvas');

    let myblob = null;

    canvas.toBlob(function (blob) {
        myblob = blob;
        file = new File([myblob], fileName + '.jpg', { type: 'image/jpeg' });
        saveFileToBlazor(file);

    }, 'image/jpeg');




}

function saveFileToBlazor(file) {

    // Create a filereader instance from FileReader() API
    var fileReader = new FileReader();


    //Add a listener to the file  reader
    fileReader.addEventListener("load", function () {
        // When the image is loaded, we want to get a Unit8Array of the file so we can stream it up to blazor
        window.base64ImageSrc = new Uint8Array(fileReader.result);

        //we add the data stream-able data to a function which our blazor code will call to get the data stream 

        window.fileDataStream = function () {
            return window.base64ImageSrc;
        };


        //Finally, invoke the save file back on our blazor page to come fetch the fileDataStream,
        //passing along the file name and type for processing purposes
        window.blazorPageHook.invokeMethodAsync('SaveFile',
            file.name,
            file.type)
            //.then(result => {
            //    alert(result);
            //});
    }, false);


    //now try to process the file's data via the file reader
    if (file) {
        fileReader.readAsArrayBuffer(file);

    } else {
        Promise.reject("No file selected");
    }
}


//window.rita = () => {
//    const player = document.getElementById('player');


//    //  const captureButton = document.getElementById('capture');

//    const constraints = {
//        video: true,
//    };

//    navigator.mediaDevices.getUserMedia(constraints).then((stream) => {
//        player.srcObject = stream;
//    });
//}

//window.EnumerateCameras = () => {
//    navigator.mediaDevices.enumerateDevices().then(function (devices) {
//        for (var i = 0; i < devices.length; i++) {
//            var device = devices[i];
//            if (device.kind === 'videoinput') {
//                var option = document.createElement('option');
//                option.value = device.deviceId;
//                option.text = device.label || 'camera ' + (i + 1);
//                document.getElementById('videoSource').appendChild(option);
//            }
//        };
//    });
//}

window.rita = () => {

    'use strict';

    var videoElement = document.querySelector('video');
    var audioSelect = document.querySelector('select#audioSource');
    var videoSelect = document.querySelector('select#videoSource');
/*    window.resolutionSelect = document.querySelector('select#resolution');*/

    audioSelect.onchange = getStream;
    videoSelect.onchange = getStream;
/*    window.resolutionSelect.onchange = changeResolution;*/



    getStream().then(getDevices).then(gotDevices);

    function getDevices() {
        // AFAICT in Safari this only gets default devices until gUM is called :/
        return navigator.mediaDevices.enumerateDevices();
    }

    function gotDevices(deviceInfos) {
        window.deviceInfos = deviceInfos; // make available to console
        console.log('Available input and output devices:', deviceInfos);
        for (const deviceInfo of deviceInfos) {
            const option = document.createElement('option');
            option.value = deviceInfo.deviceId;
            if (deviceInfo.kind === 'audioinput') {
                option.text = deviceInfo.label || `Microphone ${audioSelect.length + 1}`;
                audioSelect.appendChild(option);
            } else if (deviceInfo.kind === 'videoinput') {
                option.text = deviceInfo.label || `Camera ${videoSelect.length + 1}`;
                videoSelect.appendChild(option);
            }
        }
    }

    function getStream() {
        if (window.stream) {
            window.stream.getTracks().forEach(track => {
                track.stop();
            });
        }
        const audioSource = audioSelect.value;
        const videoSource = videoSelect.value;


        const constraints = {
            //audio: {
            //    deviceId: audioSource ? { exact: audioSource } : undefined,

            //},
            video: {
                deviceId: videoSource ? { exact: videoSource } : undefined,
                width: { ideal: 4920 },
                height: { ideal: 4080 }
                //optional:[
                //    { minWidth: 320 },
                //    { minWidth: 640 },
                //    { minWidth: 1024 },
                //    { minWidth: 1280 },
                //    { minWidth: 1920 },
                //    { minWidth: 2560 },
                //]
                //width: { ideal: resolutionSelect.value.getSettings().width },
                //height: { ideal: resolutionSelect.value.getSettings().height }
            }
        };
        return navigator.mediaDevices.getUserMedia(constraints).
            then(gotStream).catch(handleError);
    }

    //while (window.resolutionSelect.options.length > 0) {
    //    window.resolutionSelect.remove(0);
    //}

    function gotStream(stream) {

        window.stream = stream; // make stream available to console
        audioSelect.selectedIndex = [...audioSelect.options].
            findIndex(option => option.text === stream.getAudioTracks()[0].label);
        videoSelect.selectedIndex = [...videoSelect.options].
            findIndex(option => option.text === stream.getVideoTracks()[0].label);
        videoElement.srcObject = stream;

        videoElement.onloadedmetadata = function (e) {
            var highestResolution = 0;
            window.highestResolutionTrack = null;
            for (var i = 0; i < stream.getTracks().length; i++) {
                var track = stream.getTracks()[i];
                if (track.kind === 'video') {
                    var settings = track.getSettings();
                    if (settings.width * settings.height > highestResolution) {
                        highestResolution = settings.width * settings.height;
                        window.highestResolutionTrack = track;
                    }

                    //const option = document.createElement('option');
                    //option.value = track;
                    //option.text = track.getSettings().width + "x" + track.getSettings().height;
                    //window.resolutionSelect.appendChild(option);

                    //window.resolutionSelect.selectedIndex = [...window.resolutionSelect.options].findIndex(option => option.value == window.highestResolutionTrack);

                }
            }








/*            changeResolution();*/
        }



    }

    //function changeResolution() {

    //    if (window.resolutionSelect.options.length !== 0) {

    
    //       // videoElement.srcObject = window.highestResolutionTrack;

    //        window.resolutionSelect.value.enabled = true;
    //        for (var i = 0; i < stream.getTracks().length; i++) {
    //            var track = stream.getTracks()[i];
    //            if (track != window.resolutionSelect.value) {
    //                track.stop();
    //            }
    //            else {
    //                window.highestResolutionTrack = track;
    //            }



    //        }



    //    }
    //}

        function handleError(error) {
            console.error('Error: ', error);
        }

    }