"use client";

// Keep the public page's stable MapUpload import while replacing its
// single-file workflow with an independent, resumable bulk queue.
export { BulkMapUpload as MapUpload } from "./BulkMapUpload";
