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
