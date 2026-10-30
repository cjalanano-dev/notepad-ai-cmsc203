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
                    nodeGroup.setAttribute('transform', `translate(${newX - 95}, ${newY - 36})`);
                    nodeGroup.setAttribute('data-node-x', newX);
                    nodeGroup.setAttribute('data-node-y', newY);
                }

                // Update connected edges and edge labels
                document.querySelectorAll(`[data-source-id="${this.state.draggedNodeId}"]`).forEach(edge => {
                    edge.setAttribute('x1', newX);
                    edge.setAttribute('y1', newY);
                    const tX = parseFloat(edge.getAttribute('x2') || '0');
                    const tY = parseFloat(edge.getAttribute('y2') || '0');
                    const midX = (newX + tX) / 2;
                    const midY = (newY + tY) / 2;
                    const edgeId = edge.getAttribute('data-edge-id');
                    if (edgeId) {
                        const labelGroup = document.getElementById(`edge-label-${edgeId}`);
                        if (labelGroup) {
                            labelGroup.setAttribute('transform', `translate(${midX}, ${midY})`);
                        }
                    }
                });

                document.querySelectorAll(`[data-target-id="${this.state.draggedNodeId}"]`).forEach(edge => {
                    edge.setAttribute('x2', newX);
                    edge.setAttribute('y2', newY);
                    const sX = parseFloat(edge.getAttribute('x1') || '0');
                    const sY = parseFloat(edge.getAttribute('y1') || '0');
                    const midX = (sX + newX) / 2;
                    const midY = (sY + newY) / 2;
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
