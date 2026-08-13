import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

/*
 * Part of the scaffold, not of the generated application: the frontend agent never writes this
 * file and its prompt does not mention it.
 *
 * It exists because the layer gate answers "does this compile", and for five blocks that was the
 * only question asked of the frontend. `tsc --noEmit` returning zero says nothing about whether a
 * browser can open the thing — the generated React was correct and unreachable. A bundler is
 * apparatus, the same as the .csproj files next to it.
 *
 * The port is fixed so that the origin the API has to accept in Development is knowable in
 * advance; `strictPort` makes a busy port an error instead of a silent move to 5174, which would
 * put the frontend on an origin the API was never told about.
 */
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
  },
});
