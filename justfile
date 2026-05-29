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

# Copy the recon script to the device and run it, capturing output locally
recon:
    scp cr1140-recon.sh {{user}}@{{host}}:/tmp/
    ssh {{user}}@{{host}} 'sh /tmp/cr1140-recon.sh' 2>&1 | tee docs/recon.txt
