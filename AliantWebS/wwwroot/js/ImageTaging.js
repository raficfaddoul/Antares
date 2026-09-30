let GlobalObjRef;

function CreateUUID() {
    return ([1e7] + -1e3 + -4e3 + -8e3 + -1e11).replace(/[018]/g, c =>
        (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16)
    )
}

function MakeaDot(id, y, x, i, comment, defectId) {

    let goo = String(comment);
    let innd = goo.indexOf(":");
    if (innd == -1) { goo = i; }
    else {
        goo = goo[goo.indexOf(":") + 2];
    }

    return '<div id="' + defectId + '" class="vovo" style="position: absolute; top: ' + y + '%; left: ' + x + '%;" ><div id="' + id + '" class="hover-text dot" style="top: ' + y + '%; left: ' + x + '%;">' + goo + ' </div>  <span class="tooltip-text"  id = "top" >' + comment + '</span ></div>';
    //style="top: ' + y + '%; left: ' + x + '%;" 
    
}


function RegisterRemoveTagEvent() {
    $(".dot").off('dblclick').on('dblclick', function (e) {

        var imageSource = $(this).parent().siblings("img").attr('src');
        var id = $(this).attr('id');

        if (!imageSource) return;

        $(this).remove();

     
  
        return GlobalObjRef.invokeMethodAsync('SaveTag', false, imageSource, null, null,id);
    });
}

function RegisterClickTagEvent() {
    $(".vovo").off('click').on('click', function (e) {

   
        var id = $(this).attr('id');

        return GlobalObjRef.invokeMethodAsync('TagClicked', id);
    });
}


function RegisterDragTagEvent() {
    $(".vovo").draggable({
        containment: ".outfit",
        stop: function (event, ui) {
            var new_left_percd = parseInt($(this).css("left")) / ($(".outfit").width() / 100);
            var new_top_percd = parseInt($(this).css("top")) / ($(".outfit").height() / 100);
            var new_left_perc = parseInt($(this).css("left")) / ($(".outfit").width() / 100) + "%";
            var new_top_perc = parseInt($(this).css("top")) / ($(".outfit").height() / 100) + "%";
            var output = 'Top: ' + parseInt(new_top_perc) + '%, Left: ' + parseInt(new_left_perc) + '%';

            $(this).css("left", parseInt($(this).css("left")) / ($(".outfit").width() / 100) + "%");
            $(this).css("top", parseInt($(this).css("top")) / ($(".outfit").height() / 100) + "%");

            $('.output').html('CSS Position: ' + output);

            var imageSource = $(this).siblings("img").attr('src');
            //hover - text dot
            return GlobalObjRef.invokeMethodAsync('SaveTag', true, imageSource, new_left_percd, new_top_percd, $(this).find('div').attr('id'));
        }

      
    });
}



window.renderTags = (objRef,ids, xs, ys, comments,defectIds) => {
    $(".dot").remove();

    if (!xs || !ys) {
    //    RegisterRemoveTagEvent();
        return;
    }

    GlobalObjRef = objRef;

    $(".dot").remove();
    for (let i = 0; i < xs.length; i++) {

        var dot = MakeaDot(ids[i], ys[i], xs[i], i, comments[i],defectIds[i])
    //    var dot = '<div ><div id="' + ids[i] + '" class="hover-text dot" style="top: ' + ys[i] + '%; left: ' + xs[i] + '%;">' + (i) + ' </div>  <span class="tooltip-text" style="top: ' + ys[i] + '%; left: ' + xs[i] + '%;"  id = "top" > bjk ' + comments[i] + '</span ></div>';


      //  var dot = '<div id="' + ids[i] + '"  class="dot" style="top: ' + ys[i] + '%; left: ' + xs[i] + '%;">' + (i) + '</div>';
        $(dot).hide().appendTo($(".outfit img").parent()).fadeIn(350);
    }
    RegisterRemoveTagEvent();
    RegisterDragTagEvent();
    RegisterClickTagEvent();


};

window.sayHello2 = (dotNetHelper, name) => {
    return dotNetHelper.invokeMethodAsync('GetHelloMessage', name);
};

function GetPercentCoordinates(p1,e) {
    var top_offset = $(p1).offset().top - $(window).scrollTop();
    var left_offset = $(p1).offset().left - $(window).scrollLeft();

    var top_px = Math.round((e.clientY - top_offset - 12));
    var left_px = Math.round((e.clientX - left_offset - 12));

    var top_perc = top_px / $(p1).height() * 100;
    var left_perc = left_px / $(p1).width() * 100;


    return {
        Top: top_perc,
        Left: left_perc,
    };
}



function renderjQueryComponents(objRef, defectId) {
    //$(document).ready(function () {

    //    $(".outfit").hide();

    //});



    $(".outfit img").off('click').on('click', function (e) {
        var dot_count = $(".dot").length;

        let values = GetPercentCoordinates(this,e);
        top_perc = values.Top;
        left_perc = values.Left;


        // alert('Top: ' + top_px + 'px = ' + top_perc + '%');
        // alert('Left: ' + left_px + 'px = ' + left_perc + '%');

        let guid = CreateUUID();

        var dot = MakeaDot(guid, top_perc, left_perc, (dot_count + 1), (dot_count + 1), defectId)

      //  var dot = '<div id="' + guid + '" class="dot" style="top: ' + top_perc + '%; left: ' + left_perc + '%;">' + (dot_count + 1) + '</div>';

        $(dot).hide().appendTo($(this).parent()).fadeIn(350);;

        RegisterRemoveTagEvent();
        RegisterDragTagEvent();
        RegisterClickTagEvent();

        //$(".dot").draggable({
        //    containment: ".outfit",
        //    stop: function (event, ui) {
        //        var new_left_perc = parseInt($(this).css("left")) / ($(".outfit").width() / 100) + "%";
        //        var new_top_perc = parseInt($(this).css("top")) / ($(".outfit").height() / 100) + "%";
        //        var output = 'Top: ' + parseInt(new_top_perc) + '%, Left: ' + parseInt(new_left_perc) + '%';

        //        $(this).css("left", parseInt($(this).css("left")) / ($(".outfit").width() / 100) + "%");
        //        $(this).css("top", parseInt($(this).css("top")) / ($(".outfit").height() / 100) + "%");

        //        $('.output').html('CSS Position: ' + output);
        //    }
        //});

        console.log("Left: " + left_perc + "%; Top: " + top_perc + '%;');
        $('.output').html("CSS Position: Left: " + parseInt(left_perc) + "%; Top: " + parseInt(top_perc) + '%;');


        var imageSource = $(this).attr('src');

        //$('#imageContainerId').prop('src')

        return objRef.invokeMethodAsync('SaveTag', true, imageSource, left_perc, top_perc, guid);
    });

}

window.currentScale = 1;  // Default scale is 1 (no zoom)

window.BlazorCanvasInterop = {
    setupCanvas: function (dotNetRef, canvasElement) {
   
        let offsetX = 0;       // X offset for panning
        let offsetY = 0;       // Y offset for panning

        function getMousePos(evt) {
            const rect = canvasElement.getBoundingClientRect();
            return {
                x: ((evt.clientX - rect.left) - offsetX) / currentScale,
                y: ((evt.clientY - rect.top) - offsetY) / currentScale
            };
        }

        function resizeCanvas() {
            const rect = canvasElement.getBoundingClientRect();
            const ratio = window.devicePixelRatio || 1;
            canvasElement.width = rect.width * ratio;
            canvasElement.height = rect.height * ratio;

            const ctx = canvasElement.getContext('2d');
            ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
            ctx.translate(offsetX, offsetY);
            ctx.scale(currentScale, currentScale);

            // Return dimensions based on current scale and canvas size
            return {
                Width: canvasElement.width / currentScale,
                Height: canvasElement.height / currentScale
            };
        }

        // Return the initial dimensions after setting up the canvas
        const initialDimensions = resizeCanvas();

        window.addEventListener('resize', function () {
            resizeCanvas(); // You might want to handle the updated dimensions here if needed
        });

        canvasElement.addEventListener('mousedown', function (evt) {
            if (evt.button === 0) {
                const pos = getMousePos(evt);
                dotNetRef.invokeMethodAsync('StartLine', pos.x, pos.y);
            }
        });

        canvasElement.addEventListener('mouseup', function (evt) {
            if (evt.button === 0) {
                const pos = getMousePos(evt);
                dotNetRef.invokeMethodAsync('EndLine', pos.x, pos.y);
            }
        });

        return {
            setScale: function (scale) {
                currentScale = scale;
                resizeCanvas();
            },
            setOffset: function (x, y) {
                offsetX = x;
                offsetY = y;
                resizeCanvas();
            },
            Width: canvasElement.width,
            Height: canvasElement.height
        };
    },

    drawLine: function (canvasElement, x1, y1, x2, y2, color) {
        const ctx = canvasElement.getContext('2d');
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x2, y2);
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        ctx.stroke();
    }
};




function setupZoom(elementId) {
    const container = document.getElementById(elementId);
    let isPanning = false;
    let startX = 0, startY = 0;
    const ZOOM_SPEED = 0.1;

    container.addEventListener('wheel', (event) => {
        event.preventDefault();
        const deltaY = event.deltaY;

        if (deltaY < 0) { // Zoom in
            window.currentScale += ZOOM_SPEED;
        } else { // Zoom out
            window.currentScale -= ZOOM_SPEED;
        }

        window.currentScale = Math.min(Math.max(.1, window.currentScale), 4); // Limit scale
        container.style.transform = `scale(${window.currentScale})`;
        container.style.transformOrigin = 'center center';
    });

    container.addEventListener('mousedown', (event) => {
        if (event.button === 1) {  // Middle mouse button
            isPanning = true;
            startX = event.clientX;
            startY = event.clientY;
            container.style.cursor = 'grabbing';
        }
    });

    container.addEventListener('mousemove', (event) => {
        if (isPanning) {
            const deltaX = event.clientX - startX;
            const deltaY = event.clientY - startY;
            container.style.transform = `translate(${deltaX}px, ${deltaY}px) scale(${window.currentScale})`;
        }
    });

    container.addEventListener('mouseup', (event) => {
        if (event.button === 1) {
            isPanning = false;
            container.style.cursor = 'default';
        }
    });
}


function adjustMouseCoordinates(x, y) {
    return {
        adjustedX: x / window.currentScale,
        adjustedY: y / window.currentScale
    };
}

function getSvgCoordinates(clientX, clientY) {
    var svg = document.querySelector("svg");
    var pt = svg.createSVGPoint();
    pt.x = clientX;
    pt.y = clientY;
    var svgP = pt.matrixTransform(svg.getScreenCTM().inverse());
    return { x: svgP.x, y: svgP.y };
}

window.getSvgBoundingRect = function (id) {
    var el = document.getElementById(id);
    if (!el) return { left: 0, top: 0, width: 754, height: 400 };
    var r = el.getBoundingClientRect();
    return { left: r.left, top: r.top, width: r.width, height: r.height };
};

window.preventWheelPropagation = function (elementId) {
    var element = document.getElementById(elementId);
    if (element) {
        element.addEventListener('wheel', function (event) {
           // event.stopPropagation();
            event.preventDefault(); // This is optional, only if you want to prevent scrolling
        });
    }
};

window.observeSvgResize = function (elementId, dotnetRef) {
    var el = document.getElementById(elementId);
    if (!el) return null;
    var observer = new ResizeObserver(function () {
        var r = el.getBoundingClientRect();
        dotnetRef.invokeMethodAsync('OnSvgResized', r.left, r.top, r.width, r.height);
    });
    observer.observe(el);
    return { dispose: function () { observer.disconnect(); } };
};
