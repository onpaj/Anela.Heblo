#!/bin/bash

# Script to convert remaining complex Assert patterns that the first script missed

echo "Converting remaining complex Assert patterns..."

# Find all C# test files containing remaining Assert statements
find backend/test -name "*.cs" -exec grep -l "Assert\." {} \; | while read file; do
    echo "Processing remaining patterns in: $file"
    
    # Handle Assert.ThrowsAsync patterns - these are more complex to convert
    # For now, let's convert the simpler remaining patterns
    
    # Handle Assert.All - this needs manual review as it's complex
    # sed -i '' 's/Assert\.All(\([^,]*\), \([^)]*\));/\1.Should().AllSatisfy(\2);/g' "$file"
    
    # Handle Assert.IsType
    sed -i '' 's/Assert\.IsType<\([^>]*\)>(\([^)]*\));/\2.Should().BeOfType<\1>();/g' "$file"
    
    # Handle simple boolean expressions with comparisons
    # This is tricky as it involves complex regex - let's handle manually
    
done

echo "Part 2 conversion completed!"