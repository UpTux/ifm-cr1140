// SPDX-License-Identifier: GPL-3.0-only
fn main() {
    // Embed glyphs/resources for the software renderer (no system fonts at
    // runtime — the default font is baked into the binary).
    slint_build::compile_with_config(
        "ui/app.slint",
        slint_build::CompilerConfiguration::new()
            .embed_resources(slint_build::EmbedResourcesKind::EmbedForSoftwareRenderer),
    )
    .expect("compile app.slint");
}
