/* concept-map.js - Smooth Mind-Map Canvas Pan, Zoom & Node Dragging for Notepad AI */
window.conceptMapInteractions = {
    state: {
        panX: 0,
        panY: 0,
        scale: 1,
        isPanning: false,
        isDraggingNode: false,
        draggedNodeId: null,
        dragStartX: 0,
        dragStartY: 0,
        initialNodeX: 0,
        initialNodeY: 0,
        dotNetRef: null,
        initialized: false
    },

    init: function (dotNetHelper) {
        this.state.dotNetRef = dotNetHelper;
        const svg = document.getElementById('concept-map-svg');
        if (!svg) return;

        // Prevent duplicate listener attachments
        if (this.state.initialized && this.state.attachedSvg === svg) {
            this.applyTransform();
            return;
        }

        this.state.attachedSvg = svg;
        this.state.initialized = true;

        const onWheel = (e) => {
            e.preventDefault();
            const zoomFactor = e.deltaY < 0 ? 0.08 : -0.08;
            this.zoom(zoomFactor, e.clientX, e.clientY);
        };

        const onPointerDown = (e) => {
            // Check if clicking directly on a node drag target
            const nodeElem = e.target.closest('.graph-node');
            if (nodeElem) {
                const nodeIdAttr = nodeElem.getAttribute('data-node-id');
                const rawX = parseFloat(nodeElem.getAttribute('data-node-x') || '0');
                const rawY = parseFloat(nodeElem.getAttribute('data-node-y') || '0');
                if (nodeIdAttr) {
                    this.state.isDraggingNode = true;
                    this.state.draggedNodeId = parseInt(nodeIdAttr, 10);
                    this.state.dragStartX = e.clientX;
                    this.state.dragStartY = e.clientY;
                    this.state.initialNodeX = rawX;
                    this.state.initialNodeY = rawY;
                    svg.style.cursor = 'grabbing';
                    return;
                }
            }

            // Otherwise, pan the mind-map canvas
            if (e.button === 0) {
                this.state.isPanning = true;
                this.state.dragStartX = e.clientX - this.state.panX;
                this.state.dragStartY = e.clientY - this.state.panY;
                svg.style.cursor = 'grabbing';
            }
        };

        const onPointerMove = (e) => {
            if (this.state.isDraggingNode && this.state.draggedNodeId !== null) {
                // Dragging a specific mind-map concept node
                const deltaScreenX = e.clientX - this.state.dragStartX;
                const deltaScreenY = e.clientY - this.state.dragStartY;
                
                // Account for current zoom scale
                const newX = Math.round(this.state.initialNodeX + (deltaScreenX / this.state.scale));
                const newY = Math.round(this.state.initialNodeY + (deltaScreenY / this.state.scale));

                // Instantly update SVG DOM elements for 60fps buttery responsiveness
                const nodeGroup = document.getElementById(`node-g-${this.state.draggedNodeId}`);
                if (nodeGroup) {
                    const halfW = parseFloat(nodeGroup.getAttribute('data-half-w') || '90');
                    const halfH = parseFloat(nodeGroup.getAttribute('data-half-h') || '32');
                    nodeGroup.setAttribute('transform', `translate(${newX - halfW}, ${newY - halfH})`);
                    nodeGroup.setAttribute('data-node-x', newX);
                    nodeGroup.setAttribute('data-node-y', newY);
                }

                // Update connected cubic bezier tree edges and edge labels
                document.querySelectorAll(`[data-source-id="${this.state.draggedNodeId}"]`).forEach(edge => {
                    const srcHalfH = parseFloat(edge.getAttribute('data-src-half-h') || '36');
                    const sX = newX;
                    const sY = newY + srcHalfH;
                    const eX = parseFloat(edge.getAttribute('data-end-x') || '0');
                    const eY = parseFloat(edge.getAttribute('data-end-y') || '0');
                    const ctrlY = (sY + eY) / 2;

                    edge.setAttribute('data-start-x', sX);
                    edge.setAttribute('data-start-y', sY);
                    edge.setAttribute('d', `M ${sX} ${sY} C ${sX} ${ctrlY}, ${eX} ${ctrlY}, ${eX} ${eY}`);

                    const midX = (sX + eX) / 2;
                    const midY = (sY + eY) / 2;
                    const edgeId = edge.getAttribute('data-edge-id');
                    if (edgeId) {
                        const labelGroup = document.getElementById(`edge-label-${edgeId}`);
                        if (labelGroup) {
                            labelGroup.setAttribute('transform', `translate(${midX}, ${midY})`);
                        }
                    }
                });

                document.querySelectorAll(`[data-target-id="${this.state.draggedNodeId}"]`).forEach(edge => {
                    const tgtHalfH = parseFloat(edge.getAttribute('data-tgt-half-h') || '31');
                    const sX = parseFloat(edge.getAttribute('data-start-x') || '0');
                    const sY = parseFloat(edge.getAttribute('data-start-y') || '0');
                    const eX = newX;
                    const eY = newY - tgtHalfH - 4;
                    const ctrlY = (sY + eY) / 2;

                    edge.setAttribute('data-end-x', eX);
                    edge.setAttribute('data-end-y', eY);
                    edge.setAttribute('d', `M ${sX} ${sY} C ${sX} ${ctrlY}, ${eX} ${ctrlY}, ${eX} ${eY}`);

                    const midX = (sX + eX) / 2;
                    const midY = (sY + eY) / 2;
                    const edgeId = edge.getAttribute('data-edge-id');
                    if (edgeId) {
                        const labelGroup = document.getElementById(`edge-label-${edgeId}`);
                        if (labelGroup) {
                            labelGroup.setAttribute('transform', `translate(${midX}, ${midY})`);
                        }
                    }
                });

                // Debounce / send updated position to Blazor AppState
                if (this.state.dotNetRef) {
                    this.state.dotNetRef.invokeMethodAsync('OnNodeDraggedJs', this.state.draggedNodeId, newX, newY);
                }
            } else if (this.state.isPanning) {
                // Panning canvas
                this.state.panX = e.clientX - this.state.dragStartX;
                this.state.panY = e.clientY - this.state.dragStartY;
                this.applyTransform();
            }
        };

        const onPointerUp = () => {
            if (this.state.isDraggingNode) {
                this.state.isDraggingNode = false;
                this.state.draggedNodeId = null;
            }
            if (this.state.isPanning) {
                this.state.isPanning = false;
            }
            if (svg) {
                svg.style.cursor = 'grab';
            }
        };

        svg.addEventListener('wheel', onWheel, { passive: false });
        svg.addEventListener('pointerdown', onPointerDown);
        window.addEventListener('pointermove', onPointerMove);
        window.addEventListener('pointerup', onPointerUp);

        this.applyTransform();
    },

    resetTransform: function () {
        this.state.panX = 0;
        this.state.panY = 0;
        this.state.scale = 1;
        this.applyTransform();
    },

    zoom: function (delta, clientX, clientY) {
        const prevScale = this.state.scale;
        const nextScale = Math.min(Math.max(0.4, prevScale + delta), 2.5);
        
        if (clientX !== undefined && clientY !== undefined) {
            const svg = document.getElementById('concept-map-svg');
            if (svg) {
                const rect = svg.getBoundingClientRect();
                const mouseX = clientX - rect.left;
                const mouseY = clientY - rect.top;
                
                this.state.panX = mouseX - ((mouseX - this.state.panX) * (nextScale / prevScale));
                this.state.panY = mouseY - ((mouseY - this.state.panY) * (nextScale / prevScale));
            }
        }

        this.state.scale = Number(nextScale.toFixed(2));
        this.applyTransform();
    },

    zoomIn: function () {
        this.zoom(0.15);
    },

    zoomOut: function () {
        this.zoom(-0.15);
    },

    applyTransform: function () {
        const viewportGroup = document.getElementById('concept-viewport-group');
        if (viewportGroup) {
            viewportGroup.setAttribute('transform', `translate(${this.state.panX}, ${this.state.panY}) scale(${this.state.scale})`);
        }
        const zoomLabel = document.getElementById('map-zoom-level-text');
        if (zoomLabel) {
            zoomLabel.innerText = `${Math.round(this.state.scale * 100)}%`;
        }
    }
};
