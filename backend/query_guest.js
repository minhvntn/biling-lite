const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const sessions = await prisma.session.findMany({
    where: {
      status: 'ACTIVE',
      isGuest: true,
      amount: { gt: 0 }
    },
    include: {
      pc: true
    }
  });

  console.log("Unpaid Guest Sessions:");
  console.dir(sessions, { depth: null });
}

main()
  .catch(e => {
    console.error(e);
    process.exit(1);
  })
  .finally(async () => {
    await prisma.$disconnect();
  });
