import { PrismaClient } from '@prisma/client';

const prisma = new PrismaClient();

async function main() {
  const defaultGroupName = 'M\u1eb7c \u0111\u1ecbnh';

  await prisma.pcGroup.upsert({
    where: { name: defaultGroupName },
    update: {
      hourlyRate: 5000,
      isDefault: true,
    },
    create: {
      name: defaultGroupName,
      hourlyRate: 5000,
      isDefault: true,
    },
  });

  await prisma.pcGroup.updateMany({
    where: {
      name: { not: defaultGroupName },
      isDefault: true,
    },
    data: { isDefault: false },
  });

  await prisma.pricingConfig.upsert({
    where: { name: 'Default Rate' },
    update: {
      pricePerMinute: 83.33,
      isActive: true,
    },
    create: {
      name: 'Default Rate',
      pricePerMinute: 83.33,
      isActive: true,
    },
  });

  const majorTiers = [
    { name: 'S\u1eaft', min: 0, next: 200000 },
    { name: '\u0110\u1ed3ng', min: 200000, next: 500000 },
    { name: 'B\u1ea1c', min: 500000, next: 1000000 },
    { name: 'V\u00e0ng', min: 1000000, next: 2000000 },
    { name: 'B\u1ea1ch Kim', min: 2000000, next: 5000000 },
    { name: 'Ng\u1ecdc L\u1ee5c B\u1ea3o', min: 5000000, next: 10000000 },
    { name: 'Kim C\u01b0\u01a1ng', min: 10000000, next: 20000000 },
    { name: 'Cao Th\u1ee7', min: 20000000, next: 50000000 },
    { name: '\u0110\u1ea1i Cao Th\u1ee7', min: 50000000, next: 100000000 },
    { name: 'Th\u00e1ch \u0110\u1ea5u', min: 100000000, next: 200000000 },
  ];

  for (const tier of majorTiers) {
    const step = (tier.next - tier.min) / 9;
    for (let i = 1; i <= 9; i++) {
      const rankName = `${tier.name} ${i}`;
      const minTopup = Math.floor(tier.min + (i - 1) * step);
      
      await prisma.loyaltyRankConfig.upsert({
        where: { rankName },
        update: { minTopup },
        create: {
          rankName,
          minTopup,
          bonusPercent: 0,
        },
      });
    }
  }
}

main()
  .then(async () => {
    await prisma.$disconnect();
  })
  .catch(async (error) => {
    console.error(error);
    await prisma.$disconnect();
    process.exit(1);
  });
