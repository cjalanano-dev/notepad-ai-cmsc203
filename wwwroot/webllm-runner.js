// WebLLM wrapper for browser-local in-memory inference via WebGPU
// Uses @mlc-ai/web-llm via ESM CDN import

let engine = null;
let currentModel = "Qwen2.5-0.5B-Instruct-q4f16_1-MLC";
let initProgressCallbackDotNet = null;
let isInitializing = false;

export async function isWebGpuAvailable() {
    if (!navigator.gpu) {
        return false;
    }
    try {
        const adapter = await navigator.gpu.requestAdapter();
        return !!adapter;
    } catch (e) {
        return false;
    }
}

export async function initWebLlm(dotNetHelper, modelName) {
    if (modelName) {
        currentModel = modelName;
    }
    initProgressCallbackDotNet = dotNetHelper;

    if (engine) {
        return { success: true, message: "Engine already initialized" };
    }

    if (isInitializing) {
        return { success: false, message: "Engine is currently initializing" };
    }

    const hasGpu = await isWebGpuAvailable();
    if (!hasGpu) {
        return { 
            success: false, 
            message: "WebGPU is not supported or enabled in this browser. Please use Chrome/Edge or enable WebGPU." 
        };
    }

    try {
        isInitializing = true;
        // Dynamically import WebLLM from esm.sh CDN
        const webllm = await import("https://esm.sh/@mlc-ai/web-llm@0.2.78");

        const initProgressCallback = (report) => {
            console.log("[WebLLM Progress]", report.text);
            if (initProgressCallbackDotNet) {
                try {
                    initProgressCallbackDotNet.invokeMethodAsync("OnProgressUpdate", report.text, report.progress || 0);
                } catch (err) {
                    console.warn("Progress callback error:", err);
                }
            }
        };

        engine = await webllm.CreateMLCEngine(
            currentModel,
            { initProgressCallback: initProgressCallback }
        );

        isInitializing = false;
        console.log("[WebLLM] Successfully loaded model:", currentModel);
        return { success: true, message: `Loaded ${currentModel}` };
    } catch (err) {
        isInitializing = false;
        console.error("[WebLLM] Failed to initialize engine:", err);
        return { success: false, message: err.message || String(err) };
    }
}

export async function generateChat(messagesJson, temperature, maxTokens) {
    if (!engine) {
        throw new Error("WebLLM engine is not initialized. Call initWebLlm first.");
    }

    try {
        const messages = JSON.parse(messagesJson);
        const reply = await engine.chat.completions.create({
            messages: messages,
            temperature: temperature || 0.2,
            max_tokens: maxTokens || 2048,
            stream: false
        });

        if (reply && reply.choices && reply.choices.length > 0) {
            return reply.choices[0].message.content || "";
        }
        return "";
    } catch (err) {
        console.error("[WebLLM] Chat generation error:", err);
        throw err;
    }
}

export function isReady() {
    return engine !== null;
}

