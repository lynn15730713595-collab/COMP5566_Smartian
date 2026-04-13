#!/usr/bin/env python3
"""
Compile modifier_reentrancy.sol using py-solc-x
"""

import json
import os
from solcx import compile_source, install_solc, set_solc_version

# Install and set solc version 0.4.24
print("Installing solc 0.4.24...")
install_solc('0.4.24')
set_solc_version('0.4.24')

# Read the source code
source_path = "/home/lynn/COMP5566/benchmark/T1_Reentrancy/modifier_reentrancy.sol"
with open(source_path, 'r') as f:
    source_code = f.read()

print(f"Compiling {source_path}...")

# Compile the contract
compiled = compile_source(source_code, output_values=['abi', 'bin'])

# Get the contract ID (first key)
contract_id = list(compiled.keys())[0]
print(f"Contract ID: {contract_id}")

# Extract ABI and BIN
contract_data = compiled[contract_id]
abi = contract_data['abi']
bin_data = contract_data['bin']

print(f"\nABI ({len(json.dumps(abi))} chars):")
print(json.dumps(abi, indent=2))

print(f"\nBIN ({len(bin_data)} chars):")
print(bin_data[:100] + "..." if len(bin_data) > 100 else bin_data)

# Save to files
output_dir = "/home/lynn/COMP5566/benchmark/T1_Reentrancy"

abi_path = os.path.join(output_dir, "modifier_reentrancy.abi")
with open(abi_path, 'w') as f:
    json.dump(abi, f, indent=2)
print(f"\nABI saved to: {abi_path}")

bin_path = os.path.join(output_dir, "modifier_reentrancy.bin")
with open(bin_path, 'w') as f:
    f.write(bin_data)
print(f"BIN saved to: {bin_path}")

print("\nCompilation completed successfully!")