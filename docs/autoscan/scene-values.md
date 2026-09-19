# Valores de escena de `DepthOccupancyMapper` (proyecto de origen)

Al armar `AutoScanScene` en el Editor, un componente nuevo nace con los defaults del código, que
**no** son los valores afinados en `Mortuorium-test/Assets/Scenes/ArBasicScene.unity` (p. ej.
`minCellHits`: 4 acá vs 3 por defecto). Estos son los valores serializados de ese componente
(referencias de objeto omitidas — se auto-resuelven por `GetComponent`), para copiarlos a mano
en el Inspector. Los knobs de runtime se ajustan con TUNE (sólo dev build).

```yaml
  cellSize: 0.08
  bandBottom: 0.3
  bandTopMargin: 0.2
  wallNormalMax: 0.35
  minCellHits: 4
  minVerticalSpanFraction: 0.45
  lineInlierDist: 0.08
  lineIterations: 300
  minSegmentCells: 7
  minDensityFraction: 0.35
  maxRunGap: 0.4
  snapToRightAngles: 1
  snapAngleTolerance: 20
  collinearMergeAngle: 10
  dropUnattachedWalls: 1
  claimBuiltWallFootprint: 1
  wallRidgeClaimMargin: 0.3
  wallRidgeCoreKeep: 0.08
  wallOffsetCluster: 0.15
  coplanarMaxGap: 5
  wallThickness: 0.12
  maxWalls: 32
  wallTopReachFraction: 0.6
  wallColumnFillFraction: 0.45
  wallColumnMinFrames: 2
  wallClaimMargin: 0.12
  autoAnchorHits: 3
  autoAnchorTolerance: 0.2
  autoAnchorMissGrace: 1
  wallSource: 2
  seedMinArea: 0.2
  seedMinCells: 4
  seedMinDensityFraction: 0.25
  seedMinSpanFraction: 0.25
  floorEdgeMinLength: 0.3
  floorSeedMinCells: 2
  floorSeedMinDensityFraction: 0.12
  ceilingNormalMin: 0.8
  heightBinSize: 0.05
  minCeilingHits: 40
  minRoomHeight: 1.8
  maxRoomHeight: 5
  mergeMaxAngle: 12
  mergeOffsetTolerance: 0.2
  maxBridgeGap: 2.5
  voxelSize: 0.1
  furnitureBandBottom: 0.05
  minVoxelHits: 3
  minSurfaceHits: 1
  minSurfaceVoxels: 6
  surfaceMinHeight: 0.15
  surfaceMaxHeightFraction: 0.85
  surfaceFillFraction: 0.5
  minBoxDimension: 0.15
  maxBoxDimension: 2.5
  wallExcludeMargin: 0.15
  furnitureExcludeMargin: 0.1
  buildRoomGraph: 1
  joinRadius: 0.35
  junctionExtendMax: 0.6
  junctionPerpMinAngle: 15
  drawOutline: 1
```
