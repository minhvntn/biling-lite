const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function run() {
  const ranks = await prisma.loyaltyRankConfig.findMany({
    orderBy: { minTopup: 'asc' },
  });

  console.log(`Found ${ranks.length} ranks.`);

  if (ranks.length === 0) return;

  const minMinutes = 15;  // Highest rank rate
  const maxMinutes = 150; // Lowest rank rate (10x slower)

  // Reverse linear scale: 
  // Lower index (lower rank) -> Higher minutes per point
  // Higher index (higher rank) -> Lower minutes per point
  
  for (let i = 0; i < ranks.length; i++) {
    const rank = ranks[i];
    
    // Interpolation factor: 0 at index 0, 1 at index (length-1)
    const factor = ranks.length > 1 ? i / (ranks.length - 1) : 1;
    
    // minutes = max - (max - min) * factor
    // if index is 0 (lowest): minutes = 150 - 0 = 150
    // if index is last (highest): minutes = 150 - (135) = 15
    const minutes = Math.round(maxMinutes - (maxMinutes - minMinutes) * factor);

    await prisma.loyaltyRankConfig.update({
      where: { id: rank.id },
      data: { minutesPerPoint: minutes }
    });

    console.log(`Updated ${rank.rankName}: ${minutes} mins/point`);
  }

  console.log('All ranks updated.');
}

run()
  .catch(console.error)
  .finally(() => prisma.$disconnect());
