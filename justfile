host    := env_var_or_default("CR1140_HOST", "192.168.1.102")
user    := env_var_or_default("CR1140_USER", "root")
target  := env_var_or_default("CR1140_TARGET", "aarch64-unknown-linux-musl")
appdir  := env_var_or_default("CR1140_APPDIR", "/home/cds-apps")

# List recipes
default:
    @just --list

# Host-side unit tests (pure-logic modules)
test:
    cargo test

# Build an example binary (static musl by default).
# glibc escape hatch: CR1140_TARGET=aarch64-unknown-linux-gnu.2.35 just build-example <name>
build-example name:
    cargo zigbuild --release --target {{target}} --example {{name}}

# Confirm a built example is a static aarch64 ELF
verify-example name: (build-example name)
    file target/{{target}}/release/examples/{{name}}

# Copy an example to the device and run it
run-example name: (build-example name)
    ssh {{user}}@{{host}} 'mkdir -p {{appdir}}'
    scp target/{{target}}/release/examples/{{name}} {{user}}@{{host}}:{{appdir}}/
    ssh {{user}}@{{host}} '{{appdir}}/{{name}}'

# Build the Slint dashboard demo (Option B: software renderer, static musl)
build-slint:
    cargo zigbuild --release --target {{target}} -p cr1140-slint-demo

# Deploy + run the Slint demo. Stops the autostart app first so the demo gets
# exclusive ownership of /dev/fb0 (and avoids ETXTBSY overwriting a running bin).
run-slint: build-slint
    ssh {{user}}@{{host}} 'systemctl stop cr1140-app.service || true; mkdir -p {{appdir}}'
    scp target/{{target}}/release/cr1140-slint-demo {{user}}@{{host}}:{{appdir}}/
    ssh {{user}}@{{host}} '{{appdir}}/cr1140-slint-demo'

# Build the round-baler operator-panel demo (Option B: software renderer, static musl)
build-baler:
    cargo zigbuild --release --target {{target}} -p cr1140-baler-demo

# Deploy + run the baler demo. Stops the autostart app first so the demo gets
# exclusive ownership of /dev/fb0 (and avoids ETXTBSY overwriting a running bin).
run-baler: build-baler
    ssh {{user}}@{{host}} 'systemctl stop cr1140-app.service || true; mkdir -p {{appdir}}'
    scp target/{{target}}/release/cr1140-baler-demo {{user}}@{{host}}:{{appdir}}/
    ssh {{user}}@{{host}} '{{appdir}}/cr1140-baler-demo'

# Copy the recon script to the device and run it, capturing output locally
recon:
    scp scripts/cr1140-recon.sh {{user}}@{{host}}:/tmp/
    ssh {{user}}@{{host}} 'sh /tmp/cr1140-recon.sh' 2>&1 | tee docs/recon.txt

# Avalonia demo recipes (device now at 10.10.10.229, override via CR1140_HOST)
avdir := "/home/cds-apps/cr1140-avalonia-demo"

# Cross-publish the Avalonia demo from macOS to linux-arm64 (self-contained)
publish-avalonia:
    dotnet publish cr1140-avalonia-demo/Cr1140.AvaloniaDemo.csproj -c Release -r linux-arm64 --self-contained true -p:InvariantGlobalization=true -o cr1140-avalonia-demo/publish/linux-arm64

# Deploy + autostart the Avalonia demo (stops CODESYS + app-launcher + cr1140-app, enables cr1140-avalonia.service)
deploy-avalonia: publish-avalonia
    ssh {{user}}@{{host}} 'mkdir -p {{avdir}}'
    scp -r cr1140-avalonia-demo/publish/linux-arm64/* {{user}}@{{host}}:{{avdir}}/
    scp cr1140-avalonia-demo/deploy/cr1140-avalonia.service cr1140-avalonia-demo/deploy/install.sh {{user}}@{{host}}:/tmp/
    ssh {{user}}@{{host}} 'sh /tmp/install.sh'

# Quick manual run of the Avalonia demo (foreground; stops services but does NOT enable autostart)
run-avalonia: publish-avalonia
    ssh {{user}}@{{host}} 'systemctl stop cr1140-avalonia.service || true; systemctl stop cr1140-app.service || true; mkdir -p {{avdir}}'
    scp -r cr1140-avalonia-demo/publish/linux-arm64/* {{user}}@{{host}}:{{avdir}}/
    ssh {{user}}@{{host}} 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 {{avdir}}/Cr1140.AvaloniaDemo /dev/input/event1'

# Run the Avalonia demo in the desktop emulator (CR1140 bezel window on this host; no device needed)
run-emulator:
    dotnet run --project cr1140-avalonia-demo -- --emulator

# Restore stock services (unmask CODESYS/app-launcher, stop cr1140-avalonia)
restore-avalonia:
    scp cr1140-avalonia-demo/deploy/restore.sh {{user}}@{{host}}:/tmp/
    ssh {{user}}@{{host}} 'sh /tmp/restore.sh'

# --- NuGet: Cr1140.Avalonia library ---

# Pack the Cr1140.Avalonia library to dist/nuget
pack-avalonia:
    dotnet pack cr1140-avalonia/Cr1140.Avalonia.csproj -c Release -o dist/nuget

# Push packed NuGet packages to nuget.org (usage: just push-nuget $NUGET_API_KEY)
push-nuget key:
    dotnet nuget push "dist/nuget/*.nupkg" --api-key {{key}} --source https://api.nuget.org/v3/index.json --skip-duplicate
