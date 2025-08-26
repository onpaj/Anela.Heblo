#!/bin/bash

# Script to convert xUnit Assert statements to FluentAssertions
# This script will find all .cs files in the test directories and convert them

echo "Converting Assert statements to FluentAssertions..."

# Find all C# test files containing Assert statements
find backend/test -name "*.cs" -exec grep -l "Assert\." {} \; | while read file; do
    echo "Processing: $file"
    
    # Add FluentAssertions using statement if not already present
    if ! grep -q "using FluentAssertions;" "$file"; then
        # Find the last using statement and add FluentAssertions after it
        sed -i '' '/^using .*/a\
using FluentAssertions;
' "$file"
        # Remove extra blank line that might be added
        sed -i '' '/using FluentAssertions;/N; s/using FluentAssertions;\n\n/using FluentAssertions;\n/' "$file"
    fi
    
    # Convert common Assert patterns to FluentAssertions
    sed -i '' 's/Assert\.Equal(\([^,]*\), \([^)]*\));/\2.Should().Be(\1);/g' "$file"
    sed -i '' 's/Assert\.True(\([^)]*\));/\1.Should().BeTrue();/g' "$file"
    sed -i '' 's/Assert\.False(\([^)]*\));/\1.Should().BeFalse();/g' "$file"
    sed -i '' 's/Assert\.Null(\([^)]*\));/\1.Should().BeNull();/g' "$file"
    sed -i '' 's/Assert\.NotNull(\([^)]*\));/\1.Should().NotBeNull();/g' "$file"
    sed -i '' 's/Assert\.Empty(\([^)]*\));/\1.Should().BeEmpty();/g' "$file"
    sed -i '' 's/Assert\.NotEmpty(\([^)]*\));/\1.Should().NotBeEmpty();/g' "$file"
    sed -i '' 's/Assert\.Single(\([^)]*\));/\1.Should().HaveCount(1);/g' "$file"
    sed -i '' 's/Assert\.Contains(\([^,]*\), \([^)]*\));/\2.Should().Contain(\1);/g' "$file"
    sed -i '' 's/Assert\.NotEqual(\([^,]*\), \([^)]*\));/\2.Should().NotBe(\1);/g' "$file"
    
done

echo "Conversion completed!"