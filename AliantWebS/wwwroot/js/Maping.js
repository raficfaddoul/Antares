let map;

let marker;



window.markersById = {};

window.MakeMap = async (objRef, x, y,c, zoom) => {
    //setTimeout(function () {
    //    console.log("This will be logged after 2 seconds");
    //}, 700);

    map = L.map('map').setView([x, y], zoom);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(map);
    map.on('click', onMapClick);
  //  AddMarker(x, y,c);
    //var marker = L.marker([x, y]).addTo(map);

    function onMapClick(e) {
        objRef.invokeMethodAsync('OnMapClicked', e.latlng.lat, e.latlng.lng);
        marker.remove();
        marker = L.marker([e.latlng.lat, e.latlng.lng]).addTo(map);
       // alert("You clicked the map at " + e.latlng);
    }
};

window.AddMarker = async (ids,type, names, x, y, c) => {
    var SiteIcon = L.icon({
        iconUrl: 'images/maps-icon-8210.png',
        iconSize: [25, 25], // Adjusted size for a more square appearance
        iconAnchor: [12, 12], // Adjusted anchor to the center for a square icon
        popupAnchor: [0, -12] // Adjusted popup anchor point// point from which the popup should open relative to the iconAnchor
    });
    for (let i = 0; i < x.length; i++) {
        if (x[i] === null) continue;

        if (window.markersById[ids[i]]) {
            window.markersById[ids[i]].remove();
        }

        if (type[i] == "Site") {
            marker = L.marker([x[i], y[i]], { icon: SiteIcon }).addTo(map);
        }
        else {
            marker = L.marker([x[i], y[i]]).addTo(map);
        }
        marker.bindTooltip(names[i]);
        if (marker._icon) {
            marker._icon.style.filter = `hue-rotate(${c[i]}deg)`;
        }

        window.markersById[ids[i]] = marker;
    }
};

window.MoveMarker = async (id, newX, newY,zoom) => {
    for (let i = 0; i < newX.length; i++) {
        // Check if the marker exists
        if (window.markersById[id[i]]) {
            // Move the marker to the new coordinates
            const newLatLng = new L.LatLng(newX[i], newY[i]);
            window.markersById[id[i]].setLatLng(newLatLng).update();
          
        } else {
            console.error("Marker with ID " + id + " does not exist.");
        }
    }
    map = map.setView([newX[0], newY[0]], zoom);
};




